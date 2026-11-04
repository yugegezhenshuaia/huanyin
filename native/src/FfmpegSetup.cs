using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;

namespace HuanYin
{
    internal static class FfmpegSetup
    {
        public const string EmbeddedResourceName = "FFMPEG_PAYLOAD";

        public static string ExeDir
        {
            get
            {
                try
                {
                    string loc = Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(loc)) return Path.GetDirectoryName(loc);
                }
                catch { }
                try { return AppDomain.CurrentDomain.BaseDirectory; }
                catch { return Directory.GetCurrentDirectory(); }
            }
        }

        public static string HomeDir
        {
            get
            {
                string root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(root)) root = Path.GetTempPath();
                return Path.Combine(root, "HuanYin");
            }
        }

        public static string CacheBinDir { get { return Path.Combine(HomeDir, "bin"); } }
        public static string CacheFfmpeg { get { return Path.Combine(CacheBinDir, "ffmpeg.exe"); } }

        public static readonly string[] MirrorUrls = new string[]
        {
            // 7.1.1 官方构建（支持 MP4 封装 ALAC），走 GitHub 加速
            "https://ghproxy.net/https://github.com/GyanD/codexffmpeg/releases/download/7.1.1/ffmpeg-7.1.1-essentials_build.zip",
            "https://gh-proxy.com/https://github.com/GyanD/codexffmpeg/releases/download/7.1.1/ffmpeg-7.1.1-essentials_build.zip",
            // 官方直连（国外线路快）
            "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip",
            // 国内镜像：速度最快，但版本较老（ALAC 只能封装到 .mov，程序会自动切换）
            "https://registry.npmmirror.com/@ffmpeg-installer/win32-x64/-/win32-x64-4.1.0.tgz",
            // 最后兜底
            "https://ghproxy.net/https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
        };

        /// <summary>Search order: app folder (portable), local cache, PATH.</summary>
        public static string Locate()
        {
            List<string> candidates = new List<string>();
            string dir = ExeDir;
            candidates.Add(Path.Combine(dir, "ffmpeg.exe"));
            candidates.Add(Path.Combine(dir, @"ffmpeg\bin\ffmpeg.exe"));
            candidates.Add(Path.Combine(dir, @"ffmpeg\ffmpeg.exe"));
            candidates.Add(Path.Combine(dir, @"bin\ffmpeg.exe"));
            candidates.Add(CacheFfmpeg);

            foreach (string c in candidates)
            {
                try
                {
                    if (File.Exists(c)) return c;
                }
                catch { }
            }

            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
            {
                foreach (string part in path.Split(';'))
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    try
                    {
                        string p = part.Trim().Trim('"');
                        if (!Directory.Exists(p)) continue;
                        string exe = Path.Combine(p, "ffmpeg.exe");
                        if (File.Exists(exe)) return exe;
                    }
                    catch { }
                }
            }
            return null;
        }

        public static bool HasEmbeddedPayload
        {
            get
            {
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName))
                        return s != null;
                }
                catch { return false; }
            }
        }

        /// <summary>Quick check that a file actually is a working ffmpeg.</summary>
        public static bool Verify(string exePath)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = "-hide_banner -version";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(15000))
                    {
                        try { p.Kill(); } catch { }
                        return false;
                    }
                    return p.ExitCode == 0 && outp.IndexOf("ffmpeg version", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static string VersionText(string exePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exePath;
                psi.Arguments = "-hide_banner -version";
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string first = p.StandardOutput.ReadLine();
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    return (first ?? "").Trim();
                }
            }
            catch { return ""; }
        }

        public static bool InstallFromFile(string sourcePath, string ffmpegPath, out string error)
        {
            error = "";
            try
            {
                if (!Verify(sourcePath))
                {
                    error = "这个文件不是可用的 ffmpeg.exe";
                    return false;
                }
                Directory.CreateDirectory(CacheBinDir);
                if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(CacheFfmpeg), StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourcePath, CacheFfmpeg, true);
                if (!Verify(CacheFfmpeg))
                {
                    error = "复制到缓存目录后校验失败";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Extracts the ffmpeg.exe that was compiled into this executable (gzip payload).</summary>
        public static bool InstallFromEmbedded(Action<string, double> report)
        {
            try
            {
                using (Stream raw = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedResourceName))
                {
                    if (raw == null) return false;
                    if (report != null) report("正在解压内置 ffmpeg 组件…", -1);
                    Directory.CreateDirectory(CacheBinDir);
                    string tmp = CacheFfmpeg + ".tmp";
                    using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(raw, System.IO.Compression.CompressionMode.Decompress))
                    using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buf = new byte[1 << 20];
                        long total = 0;
                        int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            total += n;
                        }
                    }
                    if (File.Exists(CacheFfmpeg)) File.Delete(CacheFfmpeg);
                    File.Move(tmp, CacheFfmpeg);
                    if (report != null) report("校验内置 ffmpeg…", -1);
                    if (!Verify(CacheFfmpeg))
                    {
                        try { File.Delete(CacheFfmpeg); } catch { }
                        return false;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Downloads ffmpeg (zip or exe) from the mirror list, reporting progress.</summary>
        public static bool Download(Action<string> report, Action<double, string> progress, Func<bool> cancelled)
        {
            Directory.CreateDirectory(HomeDir);
            string workDir = Path.Combine(HomeDir, "download");
            try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            Directory.CreateDirectory(workDir);

            Exception lastError = null;

            foreach (string url in MirrorUrls)
            {
                if (cancelled != null && cancelled()) return false;
                try
                {
                    if (report != null) report("正在连接 " + Host(url) + " …");
                    string fileName = Path.GetFileName(new Uri(url).LocalPath);
                    string target = Path.Combine(workDir, fileName);
                    DownloadToFile(url, target, progress, report, cancelled);
                    if (cancelled != null && cancelled()) return false;

                    string exe = null;
                    if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exe = target;
                    }
                    else if (fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase) ||
                             fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        if (report != null) report("正在解压 ffmpeg.exe …");
                        exe = ExtractFfmpegFromTarGz(target, workDir);
                    }
                    else
                    {
                        if (report != null) report("正在解压 ffmpeg.exe …");
                        exe = ExtractFfmpegFromZip(target, workDir);
                    }

                    if (exe == null)
                    {
                        lastError = new Exception("压缩包里没有找到 ffmpeg.exe");
                        continue;
                    }

                    if (report != null) report("校验下载的 ffmpeg …");
                    if (!Verify(exe))
                    {
                        lastError = new Exception("下载的文件无法运行（可能被拦截或不完整）");
                        continue;
                    }

                    Directory.CreateDirectory(CacheBinDir);
                    if (File.Exists(CacheFfmpeg)) File.Delete(CacheFfmpeg);
                    File.Copy(exe, CacheFfmpeg, true);
                    if (!Verify(CacheFfmpeg))
                    {
                        lastError = new Exception("写入缓存目录失败");
                        continue;
                    }
                    try { Directory.Delete(workDir, true); } catch { }
                    return true;
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }

            if (report != null && lastError != null)
                report("全部下载源都失败了：" + lastError.Message);
            try { Directory.Delete(workDir, true); } catch { }
            return false;
        }

        private static string Host(string url)
        {
            try { return new Uri(url).Host; } catch { return url; }
        }

        private static void DownloadToFile(string url, string target, Action<double, string> progress, Action<string> report, Func<bool> cancelled)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) HuanYin/3.0";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            req.AllowAutoRedirect = true;
            req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK)
                    throw new Exception("HTTP " + (int)resp.StatusCode);

                long total = resp.ContentLength;
                using (Stream rs = resp.GetResponseStream())
                using (FileStream fs = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[256 * 1024];
                    long read = 0;
                    DateTime last = DateTime.MinValue;
                    System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                    int n;
                    while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        fs.Write(buf, 0, n);
                        read += n;
                        double frac = total > 0 ? (double)read / total : -1;
                        if (progress != null && (DateTime.Now - last).TotalMilliseconds > 120)
                        {
                            last = DateTime.Now;
                            progress(frac, MediaItem.FormatSize(read) + (total > 0 ? " / " + MediaItem.FormatSize(total) : ""));
                        }
                        // 太慢就换镜像，别让用户干等
                        if (clock.Elapsed.TotalSeconds > 12 && read < 512 * 1024)
                            throw new Exception("这个源太慢（" + (read / 1024) + " KB / " + (int)clock.Elapsed.TotalSeconds + " 秒），换下一个");
                        if (clock.Elapsed.TotalSeconds > 600)
                            throw new Exception("下载超时，换下一个源");
                    }
                }
            }
        }

        /// <summary>Finds ffmpeg.exe inside a .tar.gz (npm packages ship this way).</summary>
        private static string ExtractFfmpegFromTarGz(string tgzPath, string workDir)
        {
            using (FileStream fs = new FileStream(tgzPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (System.IO.Compression.GZipStream gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress))
            {
                byte[] header = new byte[512];
                while (true)
                {
                    if (!ReadExactly(gz, header, 512)) return null;

                    bool allZero = true;
                    for (int i = 0; i < 512; i++) { if (header[i] != 0) { allZero = false; break; } }
                    if (allZero) return null;   // end-of-archive marker

                    string name = System.Text.Encoding.ASCII.GetString(header, 0, 100).TrimEnd('\0').Trim();
                    long size = ParseOctal(header, 124, 12);
                    byte type = header[156];
                    long padded = (size + 511) / 512 * 512;

                    bool isFile = (type == 0 || type == (byte)'0');
                    if (isFile && name.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string outPath = Path.Combine(workDir, "ffmpeg.exe");
                        using (FileStream outFs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            CopyN(gz, outFs, size);
                        return outPath;
                    }
                    if (!Skip(gz, padded)) return null;
                }
            }
        }

        private static long ParseOctal(byte[] buf, int offset, int len)
        {
            long value = 0;
            for (int i = offset; i < offset + len; i++)
            {
                byte b = buf[i];
                if (b == 0 || b == (byte)' ') continue;
                if (b < (byte)'0' || b > (byte)'7') continue;
                value = value * 8 + (b - (byte)'0');
            }
            return value;
        }

        private static bool ReadExactly(Stream s, byte[] buf, int count)
        {
            int read = 0;
            while (read < count)
            {
                int n = s.Read(buf, read, count - read);
                if (n <= 0) return false;
                read += n;
            }
            return true;
        }

        private static void CopyN(Stream src, Stream dst, long count)
        {
            byte[] buf = new byte[1 << 20];
            long left = count;
            while (left > 0)
            {
                int n = src.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) break;
                dst.Write(buf, 0, n);
                left -= n;
            }
        }

        private static bool Skip(Stream s, long count)
        {
            byte[] buf = new byte[64 * 1024];
            long left = count;
            while (left > 0)
            {
                int n = s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                if (n <= 0) return false;
                left -= n;
            }
            return true;
        }

        /// <summary>Finds ffmpeg.exe inside a zip without needing the whole file in memory.</summary>
        private static string ExtractFfmpegFromZip(string zipPath, string workDir)
        {
            using (System.IO.Compression.ZipArchive zip = System.IO.Compression.ZipFile.OpenRead(zipPath))
            {
                foreach (System.IO.Compression.ZipArchiveEntry entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.EndsWith("/ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        string outPath = Path.Combine(workDir, "ffmpeg.exe");
                        using (Stream es = entry.Open())
                        using (FileStream fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                            es.CopyTo(fs, 1 << 20);
                        return outPath;
                    }
                }
            }
            return null;
        }
    }


}

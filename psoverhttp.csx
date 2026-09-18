/*
*
* PSOVERHTTP
*
* PowerShell over HTTP
*
* Lightweight PowerShell command execution server over HTTP.
*
* Version: 1.0.0
* Platform: Windows
* PowerShell: 2.0+
* Framework: .NET Framework 4.6
*
* AI-assisted development: ChatGPT (chatgpt.com)
*
* Language: C# Script (.csx)
* C# Version: 6
*
* A lightweight HTTP server for remote PowerShell command execution.
* Provides a simple HTTP API for running commands on Windows.
* Commands can be executed with a specified working directory.
* Returns command output, errors, exit code and execution time.
* Supports both successful and failed command execution.
* Captures standard output (stdout) from the executed process.
* Captures standard error (stderr) separately.
* Reports the final process exit code when available.
* Supports command execution with configurable timeouts.
* Preserves output produced before a timeout occurs.
* Designed for automation, scripting and AI-agent integration.
* Can be controlled using standard HTTP clients such as curl.
* Uses JSON responses for simple machine-readable integration.
* Requires no external web server or database.
* Designed to remain lightweight and easy to deploy.
*
*/

#r "Newtonsoft.Json.dll"

using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Globalization;
using Newtonsoft.Json;

class PsHttpServer
{
    // Optional debug log file.
    // Enabled with: debugencode=<file>
    static string debugFile = null;

    // Protects simultaneous writes from multiple HTTP request threads.
    static object debugLock = new object();

    public static void Main()
    {
        // Default HTTP port.
        int port = 8080;
        
        string[] args =
            Environment.GetCommandLineArgs();
        
        // Parse optional named command-line arguments.
        foreach (string arg in args)
        {
            // Optional HTTP port:
            // port=9000
            if (arg.StartsWith(
                "port=",
                StringComparison.OrdinalIgnoreCase))
            {
                int p;
        
                if (Int32.TryParse(
                    arg.Substring("port=".Length),
                    out p) &&
                    p > 0 &&
                    p <= 65535)
                {
                    port = p;
                }
            }
        }

        // Optional debugencode=<file> argument enables detailed
        // command/output encoding diagnostics.
        foreach (string arg in args)
        {
            if (arg.StartsWith(
                "debugencode=",
                StringComparison.OrdinalIgnoreCase))
            {
                debugFile =
                    arg.Substring("debugencode=".Length);
        
                if (!Path.IsPathRooted(debugFile))
                {
                    debugFile = Path.Combine(
                        Environment.CurrentDirectory,
                        debugFile);
                }
            }
        }

        if (debugFile != null)
        {
            DebugLog("=== SERVER START ===");
            DebugLog("Port: " + port);
            DebugLog("Debug file: " + debugFile);
        }

        // Create a lightweight HTTP listener.
        HttpListener listener = new HttpListener();

        listener.Prefixes.Add(
            "http://+:" + port + "/");

        listener.Start();

        Console.WriteLine(
            "PowerShell HTTP server");

        Console.WriteLine(
            "Listening: http://127.0.0.1:" +
            port +
            "/");

        if (debugFile != null)
            Console.WriteLine(
                "Debug: " + debugFile);

        Console.WriteLine(
            "Press Ctrl+C to stop.");

        // Accept HTTP requests continuously.
        while (true)
        {
            try
            {
                HttpListenerContext context =
                    listener.GetContext();

                // Handle every request on a ThreadPool thread
                // so the listener can immediately accept another request.
                ThreadPool.QueueUserWorkItem(delegate
                {
                    HandleRequest(context);
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "Listener error: " +
                    ex.Message);
            }
        }
    }

    // Reads a query parameter using explicit UTF-8 URL decoding.
    // This avoids relying on the default HttpListener query-string decoding.
    static string GetQueryParameterUtf8(
        string rawUrl,
        string name)
    {
        int q = rawUrl.IndexOf('?');

        if (q < 0)
            return null;

        string query =
            rawUrl.Substring(q + 1);

        string[] parts =
            query.Split('&');

        foreach (string part in parts)
        {
            int eq = part.IndexOf('=');

            string rawName;
            string rawValue;

            if (eq >= 0)
            {
                rawName =
                    part.Substring(0, eq);

                rawValue =
                    part.Substring(eq + 1);
            }
            else
            {
                rawName = part;
                rawValue = "";
            }

            if (UrlDecodeUtf8(rawName) == name)
                return UrlDecodeUtf8(rawValue);
        }

        return null;
    }

    // Decodes application/x-www-form-urlencoded data as UTF-8.
    static string UrlDecodeUtf8(string value)
    {
        MemoryStream ms =
            new MemoryStream();

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];

            if (c == '+')
            {
                ms.WriteByte(0x20);
            }
            else if (
                c == '%' &&
                i + 2 < value.Length &&
                IsHex(value[i + 1]) &&
                IsHex(value[i + 2]))
            {
                byte b =
                    (byte)(
                        (Hex(value[i + 1]) << 4) |
                        Hex(value[i + 2]));

                ms.WriteByte(b);

                i += 2;
            }
            else
            {
                byte[] b =
                    Encoding.UTF8.GetBytes(
                        new char[] { c });

                ms.Write(
                    b,
                    0,
                    b.Length);
            }
        }

        return Encoding.UTF8.GetString(
            ms.ToArray());
    }

    static bool IsHex(char c)
    {
        return
            (c >= '0' && c <= '9') ||
            (c >= 'A' && c <= 'F') ||
            (c >= 'a' && c <= 'f');
    }

    static int Hex(char c)
    {
        if (c >= '0' && c <= '9')
            return c - '0';

        if (c >= 'A' && c <= 'F')
            return c - 'A' + 10;

        return c - 'a' + 10;
    }

    // Processes one HTTP request.
    static void HandleRequest(
        HttpListenerContext context)
    {
        // PowerShell output is read using the current Windows
        // console OEM code page.
        Encoding consoleEncoding =
            Encoding.GetEncoding(
                CultureInfo.CurrentCulture
                    .TextInfo.OEMCodePage);

        try
        {
            // Only /command is supported by the HTTP API.
            if (context.Request.Url.AbsolutePath != "/command")
            {
                SendJson(
                    context,
                    new
                    {
                        success = false,
                        error = "Not found"
                    },
                    404);

                return;
            }

            // Command is decoded explicitly as UTF-8.
            string cmd =
                GetQueryParameterUtf8(
                    context.Request.RawUrl,
                    "cmd");

            if (String.IsNullOrEmpty(cmd))
            {
                SendJson(
                    context,
                    new
                    {
                        success = false,
                        error = "Missing cmd"
                    },
                    400);

                return;
            }

            // Default command timeout is 30 seconds.
            int timeout = 30;

            string timeoutText =
                context.Request.QueryString["timeout"];

            if (!String.IsNullOrEmpty(timeoutText))
            {
                Int32.TryParse(
                    timeoutText,
                    out timeout);
            }

            if (timeout <= 0)
                timeout = 30;

            // Resolve the requested working directory.
            string cwd =
                context.Request.QueryString["cwd"];

            string workingDirectory =
                Environment.CurrentDirectory;

            if (!String.IsNullOrEmpty(cwd))
            {
                if (Path.IsPathRooted(cwd))
                {
                    workingDirectory = cwd;
                }
                else
                {
                    workingDirectory =
                        Path.Combine(
                            Environment.CurrentDirectory,
                            cwd);
                }
            }

            if (!Directory.Exists(
                workingDirectory))
            {
                SendJson(
                    context,
                    new
                    {
                        success = false,
                        error =
                            "Working directory not found",
                        cwd = workingDirectory
                    },
                    400);

                return;
            }

            DebugLog("");
            DebugLog("=== REQUEST ===");
            DebugString("CMD", cmd);
            DebugLog(
                "CWD: " +
                workingDirectory);

            DebugLog(
                "Timeout: " +
                timeout);

            Stopwatch stopwatch =
                Stopwatch.StartNew();

            // Start PowerShell without shell execution.
            // This allows stdout/stderr to be captured directly.
            ProcessStartInfo psi =
                new ProcessStartInfo();

            psi.FileName =
                "powershell.exe";

            psi.WorkingDirectory =
                workingDirectory;

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            /*
             * PowerShell -EncodedCommand expects
             * the command as UTF-16LE Base64.
             */
            byte[] commandBytes =
                Encoding.Unicode.GetBytes(cmd);

            string encodedCommand =
                Convert.ToBase64String(
                    commandBytes);

            DebugLog(
                "EncodedCommand length: " +
                encodedCommand.Length);

            DebugLog(
                "EncodedCommand: " +
                encodedCommand);

            // Run PowerShell without profile or interactive UI.
            psi.Arguments =
                "-NoLogo " +
                "-NoProfile " +
                "-NonInteractive " +
                "-ExecutionPolicy Bypass " +
                "-EncodedCommand " +
                encodedCommand;

            Process process =
                new Process();

            process.StartInfo = psi;
            process.Start();

            DebugLog(
                "PowerShell PID: " +
                process.Id);

            StringBuilder stdout =
                new StringBuilder();

            StringBuilder stderr =
                new StringBuilder();

            // Read stdout on a separate thread so the process
            // cannot block because its output buffer becomes full.
            Thread stdoutThread =
                new Thread(
                    new ThreadStart(delegate
                    {
                        try
                        {
                            using (
                                StreamReader reader =
                                    new StreamReader(
                                        process
                                            .StandardOutput
                                            .BaseStream,
                                        consoleEncoding))
                            {
                                stdout.Append(
                                    reader.ReadToEnd());
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog(
                                "stdout read error: " +
                                ex.Message);
                        }
                    }));

            // Read stderr independently for the same reason.
            Thread stderrThread =
                new Thread(
                    new ThreadStart(delegate
                    {
                        try
                        {
                            using (
                                StreamReader reader =
                                    new StreamReader(
                                        process
                                            .StandardError
                                            .BaseStream,
                                        consoleEncoding))
                            {
                                stderr.Append(
                                    reader.ReadToEnd());
                            }
                        }
                        catch (Exception ex)
                        {
                            DebugLog(
                                "stderr read error: " +
                                ex.Message);
                        }
                    }));

            stdoutThread.Start();
            stderrThread.Start();

            // Wait until PowerShell exits or the timeout expires.
            bool finished =
                process.WaitForExit(
                    timeout * 1000);

            if (!finished)
            {
                // Stop a process that exceeded the configured timeout.
                try
                {
                    process.Kill();
                }
                catch
                {
                }

                stopwatch.Stop();

                // Give the output threads a short time to finish
                // and preserve data produced before termination.
                stdoutThread.Join(1000);
                stderrThread.Join(1000);

                DebugLog("TIMEOUT");

                DebugString(
                    "STDOUT",
                    stdout.ToString());

                DebugString(
                    "STDERR",
                    stderr.ToString());

                SendJson(
                    context,
                    new
                    {
                        success = false,
                        exitCode = (int?)null,
                        cwd = workingDirectory,
                        stdout = stdout.ToString(),
                        stderr = stderr.ToString(),
                        time =
                            stopwatch.Elapsed
                                .TotalSeconds
                                .ToString("0.000") +
                            " s",
                        timeout = true
                    },
                    408);

                return;
            }

            // Wait for both output readers to finish.
            stdoutThread.Join();
            stderrThread.Join();

            process.WaitForExit();

            stopwatch.Stop();

            int exitCode =
                process.ExitCode;

            DebugLog(
                "ExitCode: " +
                exitCode);

            DebugString(
                "STDOUT",
                stdout.ToString());

            DebugString(
                "STDERR",
                stderr.ToString());

            DebugLog(
                "Time: " +
                stopwatch.Elapsed
                    .TotalSeconds
                    .ToString("0.000") +
                " s");

            // Exit code 0 is considered successful execution.
            SendJson(
                context,
                new
                {
                    success = exitCode == 0,
                    exitCode = exitCode,
                    cwd = workingDirectory,
                    stdout = stdout.ToString(),
                    stderr = stderr.ToString(),
                    time =
                        stopwatch.Elapsed
                            .TotalSeconds
                            .ToString("0.000") +
                        " s"
                },
                200);
        }
        catch (Exception ex)
        {
            // Return unexpected server/request errors as JSON.
            DebugLog(
                "REQUEST ERROR: " +
                ex.ToString());

            SendJson(
                context,
                new
                {
                    success = false,
                    error = ex.Message
                },
                500);
        }
    }

    // Writes detailed information about a string to the debug log.
    // UTF-16 character codes are useful for diagnosing encoding problems.
    static void DebugString(
        string name,
        string value)
    {
        if (debugFile == null)
            return;

        DebugLog(
            name +
            " length: " +
            value.Length);

        DebugLog(
            name +
            ": " +
            value);

        StringBuilder codes =
            new StringBuilder();

        for (int i = 0; i < value.Length; i++)
        {
            if (i > 0)
                codes.Append(" ");

            codes.Append(
                ((int)value[i]).ToString());
        }

        DebugLog(
            name +
            " UTF16 codes: " +
            codes.ToString());
    }

    // Append one line to the optional debug file.
    static void DebugLog(string text)
    {
        if (debugFile == null)
            return;

        lock (debugLock)
        {
            try
            {
                File.AppendAllText(
                    debugFile,
                    text +
                    Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
                // Debug logging must never break the HTTP server.
            }
        }
    }

    // Serialize an object to JSON and send it as the HTTP response.
    static void SendJson(
        HttpListenerContext context,
        object value,
        int statusCode)
    {
        string json =
            JsonConvert.SerializeObject(
                value,
                Formatting.Indented);

        byte[] data =
            Encoding.UTF8.GetBytes(json);

        context.Response.StatusCode =
            statusCode;

        // Return JSON as plain text for simple HTTP clients.
        context.Response.ContentType =
            "text/plain; charset=utf-8";

        context.Response.ContentLength64 =
            data.Length;

        try
        {
            context.Response.OutputStream.Write(
                data,
                0,
                data.Length);
        }
        catch
        {
        }

        try
        {
            context.Response.OutputStream.Close();
        }
        catch
        {
        }
    }
}

PsHttpServer.Main();
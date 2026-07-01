using System;
using System.Diagnostics;
using System.IO;

namespace LfoDetector.Services;

public static class ServerService
{
    private static Process? _serverProcess;

    public static void StartServer(string serverPath)
    {
        if (!File.Exists(serverPath))
            return;

        var pythonPath = "/home/alexandr/RiderProjects/LfoDetector/.venv_cpu/bin/python3"; // тут как то надо поебаться с тем установлен питон в системе или нет

        try
        {
            _serverProcess = new Process
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = pythonPath,
                    Arguments = serverPath,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            _serverProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[Python] {e.Data}");
            };
            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    Console.WriteLine($"[Python ERROR] {e.Data}");
            };

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }

    public static void StopServer()
    {
        _serverProcess?.Kill();
    }
}
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace Utilities.EnvironmentComparator.Engine
{
    public class PowerShellScriptRunner
    {
        public async Task<string> ExecutePowerShellScriptAsync(string scriptContent, int timeoutMs = 30000)
        {
            if (string.IsNullOrWhiteSpace(scriptContent)) throw new ArgumentNullException(nameof(scriptContent));

            string encodedScript = Convert.ToBase64String(Encoding.Unicode.GetBytes(scriptContent));

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -EncodedCommand {encodedScript}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            process.OutputDataReceived += (s, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                throw new TimeoutException($"PowerShell script execution timed out after {timeoutMs}ms.");
            }

            if (process.ExitCode != 0)
            {
                string err = errorSb.ToString();
                throw new InvalidOperationException($"PowerShell script failed with exit code {process.ExitCode}: {err}");
            }

            return outputSb.ToString();
        }
    }
}

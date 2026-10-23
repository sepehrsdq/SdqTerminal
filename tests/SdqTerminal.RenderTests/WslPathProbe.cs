using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using SdqTerminal;

internal static class WslPathProbe
{
    public static void Run(string distro)
    {
        var resolver=typeof(MainWindow).Assembly.GetType("SdqTerminal.WslPathResolver")!;
        var convert=resolver.GetMethod("ConvertAsync")!;
        var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","wsl-path-probe");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,"a b'&$%.png");File.WriteAllText(file,"");
        try
        {
            var path=((Task<string>)convert.Invoke(null,new object?[]{file,distro})!).GetAwaiter().GetResult();
            string Exec(params string[] args)
            {
                var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"wsl.exe")) {UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
                foreach(var pair in TerminalEnvironment.Create(Environment.GetEnvironmentVariables())) info.Environment[pair.Key]=pair.Value;
                foreach(var arg in new[]{"--distribution",distro,"--exec"}.Concat(args))info.ArgumentList.Add(arg);
                using var p=Process.Start(info)!;
                var output=p.StandardOutput.ReadToEndAsync();var errors=p.StandardError.ReadToEndAsync();
                if(!p.WaitForExit(15000)){p.Kill(true);throw new Exception("WSL probe timed out");}
                if(p.ExitCode!=0)throw new Exception(errors.GetAwaiter().GetResult());return output.GetAwaiter().GetResult();
            }
            if(Exec("sh","-c","printf '%s' "+WslPath.Quote(path))!=path)throw new Exception("WSL path quoting changed the file name");
            if(Exec("test","-f",path)!="")throw new Exception("Converted file was not found in WSL");
            if(Exec("printenv","COLORTERM").Trim()!="truecolor"||Exec("printenv","TERM_PROGRAM").Trim()!="SdqTerminal")throw new Exception("WSL did not inherit terminal color capabilities");
            var share=@"\\wsl.localhost\"+distro+@"\home\probe";
            if(((Task<string>)convert.Invoke(null,new object?[]{share,distro})!).GetAwaiter().GetResult()!="/home/probe")throw new Exception("WSL share conversion failed");
            Console.WriteLine("PASS real WSL wslpath conversion, special-character shell round trip, file access, same-distro shares and truecolor forwarding");
        }
        finally {File.Delete(file);Directory.Delete(directory);}
    }
}

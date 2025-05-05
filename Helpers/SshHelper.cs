using System.Globalization;
using Renci.SshNet;
using ClusterSharp.Api.Models;

namespace ClusterSharp.Api.Helpers;

public static class SshHelper
{
    public static List<CommandResult>? ExecuteControllerCommands(IEnumerable<string> commands)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        return clusterInfo == null ? null : ExecuteCommands(clusterInfo.ControllerHostname, commands);
    }
    
    public static List<CommandResult>? ExecuteCommands(string hostname, IEnumerable<string> commands)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        var results = new List<CommandResult>();
        
        try
        {
            using var client = new SshClient(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
            client.Connect();

            foreach (var command in commands)
            {
                var result = new CommandResult
                {
                    Command = command,
                    Status = Constants.NotOk
                };
                
                try
                {
                    var finalCommand = command;
                    if (command.StartsWith("sudo "))
                        finalCommand = $"echo '{clusterInfo.Admin.Password}' | sudo -S {command.Substring(5)}";

                    using var cmd = client.CreateCommand(finalCommand);
                    cmd.Execute();

                    result.Output = cmd.Result;
                    result.Error = cmd.Error;
                    result.Status = cmd.ExitStatus == 0 ? Constants.Ok : Constants.NotOk;
                }
                catch (Exception ex)
                {
                    result.Error = $"Exception: {ex.Message}";
                }
                
                results.Add(result);
            }

            client.Disconnect();
        }
        catch (Exception ex)
        {
            results.Add(new CommandResult
            {
                Command = "SSH Connection",
                Status = Constants.NotOk,
                Error = $"SSH connection error: {ex.Message}"
            });
        }
        
        return results;
    }

    public static List<string> GetDockerContainers(string hostname, string username, string password)
    {
        var containers = new List<string>();
        try
        {
            using var client = new SshClient(hostname, username, password);
            client.Connect();

            using var cmd = client.CreateCommand("docker ps --format '{{.Names}}'");
            cmd.Execute();

            var output = cmd.Result;
            var error = cmd.Error;

            if (cmd.ExitStatus == 0)
            {
                containers.AddRange(
                    output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(name => name.Trim('\''))
                );
            }
            else if (!string.IsNullOrEmpty(error))
            {
                // Optionally handle error
            }

            client.Disconnect();
        }
        catch (Exception)
        {
            // Error handling if needed
        }
        return containers;
    }


    private static string ExtractFirstHostPort(string portsString)
    {
        if (string.IsNullOrWhiteSpace(portsString)) return string.Empty;

        var parts = portsString.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            var arrowIdx = trimmed.IndexOf("->", StringComparison.OrdinalIgnoreCase);
            if (arrowIdx > 0)
            {
                var left = trimmed.Substring(0, arrowIdx);
                var colonIdx = left.LastIndexOf(':');
                if (colonIdx >= 0 && colonIdx < left.Length - 1)
                {
                    var hostPort = left.Substring(colonIdx + 1);
                    if (int.TryParse(hostPort,  CultureInfo.InvariantCulture, out _))
                        return hostPort;
                }
            }
        }
        return string.Empty;
    }

    public static List<ContainerInfo>? GetDockerContainerStats(string hostname)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        var containers = new List<ContainerInfo>();
        try
        {
            using var client = new SshClient(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
            client.Connect();
            var portMap = new Dictionary<string, string>();
            using (var psCmd = client.CreateCommand("docker ps --format '{{.Names}}|{{.Ports}}'"))
            {
                psCmd.Execute();
                var psOutput = psCmd.Result;
                foreach (var line in psOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length == 2)
                    {
                        portMap[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            var format = "{{.Name}}|{{.CPUPerc}}|{{.MemUsage}}|{{.BlockIO}}";
            using var cmd = client.CreateCommand($"docker stats --no-stream --format '{format}'");
            cmd.Execute();
            var output = cmd.Result;
            var error = cmd.Error;
            if (cmd.ExitStatus == 0)
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 4)
                    {
                        var memValue = parts[2].Trim();
                        double percent = 0;
                        try
                        {
                            var memSplit = memValue.Split('/');
                            if (memSplit.Length == 2)
                            {
                                var usedStr = memSplit[0].Trim();
                                var totalStr = memSplit[1].Trim();
                                var used = ParseMemoryValue(usedStr);
                                var total = ParseMemoryValue(totalStr);
                                if (total > 0)
                                    percent = Math.Round(used / total * 100.0, 2);
                            }
                        }
                        catch(Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }

                        var name = parts[0];
                        portMap.TryGetValue(name, out var externalPortRaw);
                        var externalPort = ExtractFirstHostPort(externalPortRaw ?? string.Empty);
                        containers.Add(new ContainerInfo
                        {
                            Name = name,
                            CPU = parts[1],
                            Memory = new MemStat { Value = memValue, Percentage = percent },
                            Disk = new DiskStat { Value = parts[3], Percentage = 0 },
                            ExternalPort = externalPort
                        });
                    }
                }
            }
            client.Disconnect();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
        return containers;
    }

    private static double ParseMemoryValue(string memStr)
    {
        memStr = memStr.Trim();
        double multiplier = 1;
        if (memStr.EndsWith("GiB", StringComparison.OrdinalIgnoreCase)) multiplier = 1024;
        else if (memStr.EndsWith("MiB", StringComparison.OrdinalIgnoreCase)) multiplier = 1;
        else if (memStr.EndsWith("MB", StringComparison.OrdinalIgnoreCase)) multiplier = 1;
        else if (memStr.EndsWith("GB", StringComparison.OrdinalIgnoreCase)) multiplier = 1024;
        var numPart = new string(memStr.TakeWhile(c => char.IsDigit(c) || c == '.' || c == ',').ToArray());
        if (double.TryParse(numPart.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var value))
            return value * multiplier;
        return 0;
    }

    public static MachineStats? GetMachineStats(string hostname)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        try
        {
            using var client = new SshClient(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
            client.Connect();

            var statsCommand = @"echo ""CPU $(LC_ALL=C top -bn1 | grep ""Cpu(s)"" | sed ""s/.*, *\([0-9.]*\)%* id.*/\1/"" | awk '{printf(""%.1f%%"", 100-$1)}')  RAM $(free -m | awk '/Mem:/ { printf(""%.1f%%"", $3/$2*100) }')  DISK $(df -h / | awk 'NR==2 {print $5}')"" ";
            using var cmd = client.CreateCommand(statsCommand);
            cmd.Execute();
            
            var result = cmd.Result.Trim();
            Console.WriteLine(result);
            var stats = new MachineStats();
            
            if (!string.IsNullOrEmpty(result))
            {
                var cpuMatch = System.Text.RegularExpressions.Regex.Match(result, @"CPU\s+(\d+\.\d+)%");
                if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var cpu))
                {
                    stats.CPU = cpu;
                }

                var ramMatch = System.Text.RegularExpressions.Regex.Match(result, @"RAM\s+(\d+\.\d+)%");
                if (ramMatch.Success && double.TryParse(ramMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ram))
                {
                    stats.Memory = new MemStat 
                    { 
                        Value = $"{ram}%", 
                        Percentage = ram 
                    };
                }
                
                var diskMatch = System.Text.RegularExpressions.Regex.Match(result, @"DISK\s+(\d+)%");
                if (diskMatch.Success && double.TryParse(diskMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var disk))
                {
                    stats.Disk = new DiskStat 
                    { 
                        Value = $"{disk}%", 
                        Percentage = disk 
                    };
                }
            }

            client.Disconnect();
            return stats;
        }
        catch (Exception)
        {
            // Error handling if needed
            return null;
        }
    }
}
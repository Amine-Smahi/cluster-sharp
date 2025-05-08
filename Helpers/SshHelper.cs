using System.Globalization;
using System.Collections.Concurrent;
using Renci.SshNet;
using ClusterSharp.Api.Models.Commands;
using ClusterSharp.Api.Models.Stats;
using ClusterSharp.Api.Shared;
using System.Text.RegularExpressions;

namespace ClusterSharp.Api.Helpers;

public static class SshHelper
{
    // Connection pool to reuse SSH connections
    private static readonly ConcurrentDictionary<string, SshClient> ConnectionPool = new();
    private static readonly object PoolLock = new();
    
    private static SshClient GetOrCreateConnection(string hostname, string username, string password)
    {
        var key = $"{username}@{hostname}";
        
        if (ConnectionPool.TryGetValue(key, out var client) && client.IsConnected)
            return client;
        
        lock (PoolLock)
        {
            // Check again inside the lock to avoid race conditions
            if (ConnectionPool.TryGetValue(key, out client) && client.IsConnected)
                return client;
                
            // Create new connection if needed
            client = new SshClient(hostname, username, password);
            try
            {
                client.Connect();
                ConnectionPool[key] = client;
                return client;
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }
    
    // Cleanup method to be called during application shutdown
    public static void CleanupConnections()
    {
        foreach (var client in ConnectionPool.Values)
        {
            try
            {
                if (client.IsConnected)
                    client.Disconnect();
                client.Dispose();
            }
            catch
            {
                // Ignore errors during cleanup
            }
        }
        ConnectionPool.Clear();
    }

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
            var client = GetOrCreateConnection(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);

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

    public static List<ContainerStats>? GetDockerContainerStats(string hostname)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        var containers = new List<ContainerStats>();
        try
        {
            var client = GetOrCreateConnection(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);
            
            // Get all container info in a single command to reduce round trips
            var statsCommand = 
                "docker ps --format '{{.Names}}|{{.Ports}}' && " + 
                "echo '---STATS_SEPARATOR---' && " +
                "docker stats --no-stream --format '{{.Name}}|{{.CPUPerc}}|{{.MemPerc}}' && " +
                "echo '---STATS_SEPARATOR---' && " +
                "docker ps -s --format '{{.Names}}|{{.Size}}'";
                
            using var batchCmd = client.CreateCommand(statsCommand);
            batchCmd.Execute();
            
            var sections = batchCmd.Result.Split("---STATS_SEPARATOR---", StringSplitOptions.RemoveEmptyEntries);
            if (sections.Length != 3)
                return containers;
                
            // Parse container names and ports
            var portMap = new Dictionary<string, string>();
            foreach (var line in sections[0].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                {
                    portMap[parts[0].Trim()] = parts[1].Trim();
                }
            }
            
            // Parse CPU and memory stats
            var statsMap = new Dictionary<string, (double Cpu, double Memory)>();
            foreach (var line in sections[1].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 3)
                {
                    var name = parts[0].Trim();
                    double.TryParse(parts[1].TrimEnd('%'), CultureInfo.InvariantCulture, out var cpu);
                    double.TryParse(parts[2].TrimEnd('%'), CultureInfo.InvariantCulture, out var memory);
                    statsMap[name] = (cpu, memory);
                }
            }
            
            // Parse disk stats
            var diskMap = new Dictionary<string, string>();
            foreach (var line in sections[2].Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length == 2)
                {
                    diskMap[parts[0].Trim()] = parts[1].Trim();
                }
            }
            
            // Combine all data
            foreach (var name in statsMap.Keys)
            {
                portMap.TryGetValue(name, out var portsString);
                diskMap.TryGetValue(name, out var diskUsage);
                
                var stats = statsMap[name];
                containers.Add(new ContainerStats
                {
                    Name = name,
                    Cpu = stats.Cpu,
                    Memory = stats.Memory,
                    Disk = diskUsage ?? "0B",
                    ExternalPort = ExtractFirstHostPort(portsString ?? string.Empty)
                });
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
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

    public static MachineStats? GetMachineStats(string hostname)
    {
        var clusterInfo = ClusterHelper.GetClusterSetup();
        if (clusterInfo == null)
            return null;
        
        try
        {
            var client = GetOrCreateConnection(hostname, clusterInfo.Admin.Username, clusterInfo.Admin.Password);

            var statsCommand = @"echo ""CPU $(LC_ALL=C top -bn1 | grep ""Cpu(s)"" | sed ""s/.*, *\([0-9.]*\)%* id.*/\1/"" | awk '{printf(""%.1f%%"", 100-$1)}')  RAM $(free -m | awk '/Mem:/ { printf(""%.1f%%"", $3/$2*100) }')  DISK $(df -h / | awk 'NR==2 {print $5}')"" ";
            using var cmd = client.CreateCommand(statsCommand);
            cmd.Execute();
            
            var result = cmd.Result.Trim();
            var stats = new MachineStats();
            
            if (!string.IsNullOrEmpty(result))
            {
                var cpuMatch = Regex.Match(result, @"CPU\s+(\d+\.\d+)%");
                if (cpuMatch.Success && double.TryParse(cpuMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var cpu))
                    stats.Cpu = cpu;

                var ramMatch = Regex.Match(result, @"RAM\s+(\d+\.\d+)%");
                if (ramMatch.Success && double.TryParse(ramMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var ram))
                    stats.Memory = ram;
                
                var diskMatch = Regex.Match(result, @"DISK\s+(\d+)%");
                if (diskMatch.Success && double.TryParse(diskMatch.Groups[1].Value, CultureInfo.InvariantCulture, out var disk))
                    stats.Disk = disk;
            }

            return stats;
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
            return null;
        }
    }
}
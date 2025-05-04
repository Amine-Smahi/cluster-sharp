using ClusterSharp.Api.Models;

namespace ClusterSharp.Api.Helpers;

public static class CommandHelper
{
    public static string[] GetDeploymentCommands(string repository) => GetCommands("Assets/deploy-commands.json");

    public static string[] GetSetupCommands() => GetCommands("Assets/setup-machine-commands.json");
    public static string[] GetClusterUpdateCommands() => GetCommands("Assets/refresh-cluster-setup-commands.json");

    private static string[] GetCommands(string path)
    {
        var result = FileHelper.GetContentFromFile<CommandsData>(path, out var errorMessage);
        if (result == null)
            Console.WriteLine(errorMessage);
        return result?.Commands ?? [];
    } 
}
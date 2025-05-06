namespace ClusterSharp.Api.Helpers;

public static class CommandHelper
{
    public static List<string> GetDeploymentCommands(string repository) =>
        GetCommands("Assets/deploy-project-commands.json",
            new Dictionary<string, string> { ["<repository>"] = repository });

    public static List<string> GetSetupCommands(string hostname) =>
        GetCommands("Assets/setup-machine-commands.json",
            new Dictionary<string, string> { ["<hostname>"] = hostname });

    public static List<string> GetClusterUpdateCommands() => GetCommands("Assets/refresh-cluster-setup-commands.json");
    public static List<string> GetUpdateMachineCommands() => GetCommands("Assets/update-machine-commands.json");

    private static List<string> GetCommands(string path, Dictionary<string, string>? @override = null)
    {
        var result = FileHelper.GetContentFromFile<List<string>>(path, out var errorMessage);
        if (result == null)
        {
            Console.WriteLine(errorMessage);
            return [];
        }

        if (@override == null)
            return result;

        result = result
            .Select(line => @override.Aggregate(line, (current, pair) => current.Replace(pair.Key, pair.Value)))
            .ToList();

        return result;
    }
}
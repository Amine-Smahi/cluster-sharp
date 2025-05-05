using System.Text.Json;

namespace ClusterSharp.Api.Helpers;

public static class FileHelper
{
    public static T? GetContentFromFile<T>(string filePath, out string? errorMessage) where T : class
    {
        errorMessage = null;
        try
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            var content = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(content);
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
        }

        return null;
    }

    public static void SetContentToFile<T>(string filePath, T data, out string? errorMessage)
    {
        errorMessage = null;
        try
        {
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(filePath, json);
        }
        catch (Exception e)
        {
            errorMessage = e.Message;
        }
    }
}
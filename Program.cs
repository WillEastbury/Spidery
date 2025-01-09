using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using System.Linq;
using System.Collections.Generic;
using Azure.Storage.Blobs;
using Azure.Identity;
using System.Text.RegularExpressions;
using System.ComponentModel.DataAnnotations;

class Program
{
    static async Task Main(string[] args)
    {
       Console.WriteLine("Starting resource downloading and uploading...");
        string url;
        if (args.Length < 1)
        {
            Console.WriteLine("Please provide a URL.");
            url = Console.ReadLine();
        }
        else
        {
            url = args[0]; // The target URL is now passed as the first parameter
        }
        
        string outputDirectory = "DownloadedResources";

        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await SpiderPageAsync(url, outputDirectory);

        Console.WriteLine("Resource downloading and uploading completed.");
    }

    static async Task SpiderPageAsync(string url, string outputDirectory)
    {
        using HttpClient client = new HttpClient();

        try
        {
            string html = await client.GetStringAsync(url);

            HtmlDocument doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Extract resources
            var resources = ExtractResources(doc, url);

            // Check for self.fetchTextDataAsync calls
            var apiCalls = ExtractApiCalls(html, url);

            // Make API calls
            foreach (var apiUrl in apiCalls)
            {
                try
                {
                    Console.WriteLine($"Calling API: {apiUrl}");
                    string response = await client.GetStringAsync(apiUrl);
                    string localPath = Path.Combine(outputDirectory, Guid.NewGuid().ToString() + ".json");
                    await File.WriteAllTextAsync(localPath, response);

                    Console.WriteLine($"API response saved to: {localPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to call API {apiUrl}: {ex.Message}");
                }
            }

            // Download resources
            foreach (var resource in resources)
            {
                try
                {
                    string relativePath = resource.Replace(url, "").TrimStart('/');
                    string localPath = Path.Combine(outputDirectory, relativePath);

                    // Ensure directories exist
                    string directoryPath = Path.GetDirectoryName(localPath);
                    if (!string.IsNullOrEmpty(directoryPath))
                    {
                        Directory.CreateDirectory(directoryPath);
                    }

                    // Add .html extension for downloaded pages
                    if (!Path.HasExtension(localPath))
                    {
                        localPath += ".html";
                    }

                    Console.WriteLine($"Downloading: {resource}");

                    byte[] data = await client.GetByteArrayAsync(resource);
                    await File.WriteAllBytesAsync(localPath, data);

                    Console.WriteLine($"Saved to: {localPath}");

                   
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to process {resource}: {ex.Message}");
                }
            }

            // Check for robots.txt
            await PullFile(url, outputDirectory, "robots.txt");
            await PullFile(url, outputDirectory, "sitemap.xml");
            await PullFile(url, outputDirectory, "site.webmanifest");
            await PullFile(url, outputDirectory, "favicon.ico");

            Console.WriteLine("All resources downloaded and uploaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task PullFile(string baseUrl, string outputDirectory, string file)
    {
        try
        {
            using HttpClient client = new HttpClient();
            Uri baseUri = new Uri(baseUrl);
            Uri fileUri = new Uri(baseUri, file);

            Console.WriteLine($"Checking for {file} at: {fileUri}");

            HttpResponseMessage response = await client.GetAsync(fileUri);

            if (response.IsSuccessStatusCode)
            {
                string robotsContent = await response.Content.ReadAsStringAsync();
                string localPath = Path.Combine(outputDirectory, file);
                await File.WriteAllTextAsync(localPath, robotsContent);

                Console.WriteLine($" {file} saved to: {localPath}");
            }
            else
            {
                Console.WriteLine($"No {file} file found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to download {file}: {ex.Message}");
        }
    }

    static IEnumerable<string> ExtractResources(HtmlDocument doc, string baseUrl)
    {
        var resources = new List<string>();
        Uri baseUri = new Uri(baseUrl);

        // Find image, CSS, and JS references
        var nodes = doc.DocumentNode.SelectNodes("//img[@src] | //link[@href] | //script[@src] | //a[@href]");

        if (nodes != null)
        {
            foreach (var node in nodes)
            {
                string attribute = node.Name switch
                {
                    "img" => "src",
                    "link" => "href",
                    "script" => "src",
                    "a" => "href",
                    _ => null
                };

                if (attribute != null && node.Attributes[attribute] != null)
                {
                    string resource = node.Attributes[attribute].Value;

                    if (!string.IsNullOrWhiteSpace(resource))
                    {
                        Uri resourceUri;
                        if (Uri.TryCreate(resource, UriKind.Absolute, out resourceUri))
                        {
                            if (resourceUri.Host == baseUri.Host)
                            {
                                resources.Add(resourceUri.ToString());
                            }
                        }
                        else
                        {
                            resources.Add(new Uri(baseUri, resource).ToString());
                        }
                    }
                }
            }
        }

        return resources.Distinct();
    }

    static IEnumerable<string> ExtractApiCalls(string html, string baseUrl)
    {
        var apiUrls = new List<string>();
        Uri baseUri = new Uri(baseUrl);

        // Regex to find self.fetchTextDataAsync calls
        var regex = new Regex(@"self\.fetchTextDataAsync\(\s*`([^`]*)`,", RegexOptions.Compiled);
        var matches = regex.Matches(html);

        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                string relativeUrl = match.Groups[1].Value;
                Uri fullUrl = new Uri(baseUri, relativeUrl);
                apiUrls.Add(fullUrl.ToString());
            }
        }

        return apiUrls;
    }


}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Application.Interfaces;
using Infrastructure.Clients;
using Infrastructure.Options;
using Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;

// This script verifies the ExternalSalesService tool by calling it directly.
// To run: dotnet run --project scratch/ToolTester.csproj

namespace ToolTester;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("--- Tool Health Check ---");
        
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddMemoryCache();
        
        // Mock Options
        services.AddSingleton<IOptions<ExternalSalesApiOptions>>(Options.Create(new ExternalSalesApiOptions
        {
            BaseUrl = "https://dummyjson.com",
            TimeoutSeconds = 10,
            UseMockFallback = true
        }));
        
        services.AddHttpClient<IExternalSalesApiClient, ExternalSalesApiClient>(client => 
        {
            client.BaseAddress = new Uri("https://dummyjson.com");
        });
        
        services.AddSingleton<MockSalesService>();
        services.AddSingleton<ISalesService, ExternalSalesService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var salesService = serviceProvider.GetRequiredService<ISalesService>();
        
        try
        {
            Console.WriteLine("Calling GetSalesDataAsync()...");
            var data = await salesService.GetSalesDataAsync("6months");
            Console.WriteLine($"SUCCESS: Received {data?.Count ?? 0} items.");
            
            if (data != null && data.Any())
            {
                var first = data.First();
                Console.WriteLine($"Sample item: {first.Date} - {first.Revenue} USD - {first.Region}");
            }
            else
            {
                Console.WriteLine("WARNING: Received empty data.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAILURE: Tool crashed with: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
        
        Console.WriteLine("--- Check Complete ---");
    }
}

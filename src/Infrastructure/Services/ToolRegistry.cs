/*
 * Lớp ToolRegistry đóng vai trò là "trung tâm điều phối" các công cụ (tools) của AI:
 * 1. Lưu trữ: Quản lý danh sách các công cụ (ITool) đã được tạo và đăng ký vào hệ thống.
 * 2. Định dạng: Chuyển đổi thông tin các công cụ sang cấu trúc JSON Schema (ToolDefinition) 
 *    để các mô hình AI có thể hiểu được chức năng và cách gọi các công cụ này.
 * 3. Thực thi: Tiếp nhận yêu cầu gọi công cụ từ AI, giải mã tham số, thực hiện logic nghiệp vụ 
 *    của công cụ và trả về kết quả dưới dạng JSON cho AI xử lý tiếp.
 */
using System.Text.Json;
using Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Services;

/// <summary>
/// Tool Registry implementation - manages and executes AI tools
/// </summary>
public class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();
    private readonly ILogger<ToolRegistry> _logger;

    public ToolRegistry(
        IEnumerable<ITool> tools,
        ILogger<ToolRegistry> logger)
    {
        _logger = logger;
        
        // Register all provided tools
        foreach (var tool in tools)
        {
            _tools[tool.Name] = tool;
            _logger.LogInformation("Registered tool: {ToolName}", tool.Name);
        }
    }

    public List<ToolDefinition> GetToolDefinitions()
    {
        return _tools.Values.Select(t => new ToolDefinition
        {
            Type = "function",
            Function = new FunctionDefinition
            {
                Name = t.Name,
                Description = t.Description,
                Parameters = t.GetParametersSchema()
            }
        }).ToList();
    }

    public async Task<string> ExecuteToolAsync(string toolName, string arguments, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            throw new ArgumentException($"Tool '{toolName}' is not registered");
        }

        _logger.LogInformation("Executing tool: {ToolName} with arguments: {Arguments}", toolName, arguments);

        try
        {
            // Parse JSON arguments
            var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(arguments) 
                ?? new Dictionary<string, object>();

            // Execute the tool
            var result = await tool.ExecuteAsync(argsDict, cancellationToken);

            // Serialize result to JSON
            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tool: {ToolName}", toolName);
            
            var errorResult = new { error = ex.Message };
            return JsonSerializer.Serialize(errorResult, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }

    public bool IsToolRegistered(string toolName)
    {
        return _tools.ContainsKey(toolName);
    }
}
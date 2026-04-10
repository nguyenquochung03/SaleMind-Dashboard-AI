/*
 * Lớp InputSanitizer đảm bảo an toàn cho dữ liệu đầu vào của hệ thống:
 * 1. Làm sạch (Sanitize): Loại bỏ các ký tự nguy hiểm và giới hạn độ dài chuỗi để ngăn chặn các cuộc tấn công.
 * 2. Kiểm tra (Validate): Sử dụng các biểu thức chính quy (Regex) để phát hiện và ngăn chặn các mẫu tấn công 
 *    phổ biến như SQL Injection, XSS (Script Injection) và Command Injection.
 * 3. Bảo mật: Đảm bảo rằng mọi yêu cầu từ người dùng đều hợp lệ và không chứa mã độc trước khi đưa vào xử lý sâu hơn.
 */
using System.Text.RegularExpressions;

namespace Infrastructure.Services;

/// <summary>
/// Input sanitization service for security - prevents injection attacks
/// </summary>
public interface IInputSanitizer
{
    /// <summary>
    /// Sanitize user input to prevent injection attacks
    /// </summary>
    string Sanitize(string input);
    
    /// <summary>
    /// Validate that input doesn't contain malicious patterns
    /// </summary>
    bool IsValidInput(string input, out string? errorMessage);
}

public class InputSanitizer : IInputSanitizer
{
    // Maximum input length to prevent abuse
    private const int MaxInputLength = 1000;
    
    // Patterns that may indicate injection attempts
    private static readonly Regex[] DangerousPatterns = new[]
    {
        // SQL injection patterns
        new Regex(@"(\b(SELECT|INSERT|UPDATE|DELETE|DROP|ALTER|CREATE|EXEC|EXECUTE)\b)", RegexOptions.IgnoreCase),
        new Regex(@"(--|;|'|""|\*)", RegexOptions.IgnoreCase),
        
        // Script injection patterns
        new Regex(@"(<script|javascript:|on\w+\s*=)", RegexOptions.IgnoreCase),
        
        // Path traversal patterns
        new Regex(@"(\.\.\/|\.\.\\|%2e%2e%2f|%2e%2e\/)", RegexOptions.IgnoreCase),
        
        // Command injection patterns
        new Regex(@"(\||;|&&|\$\(|`)", RegexOptions.IgnoreCase)
    };

    public string Sanitize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Trim and limit length
        var sanitized = input.Trim();
        if (sanitized.Length > MaxInputLength)
        {
            sanitized = sanitized.Substring(0, MaxInputLength);
        }

        // Remove potentially dangerous characters
        sanitized = Regex.Replace(sanitized, @"[<>""'&\\]", string.Empty);
        
        return sanitized;
    }

    public bool IsValidInput(string input, out string? errorMessage)
    {
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            errorMessage = "Dữ liệu đầu vào không được để trống";
            return false;
        }

        if (input.Length > MaxInputLength)
        {
            errorMessage = $"Dữ liệu đầu vào vượt quá độ dài tối đa {MaxInputLength} ký tự";
            return false;
        }

        // Check for dangerous patterns
        foreach (var pattern in DangerousPatterns)
        {
            if (pattern.IsMatch(input))
            {
                errorMessage = "Dữ liệu đầu vào chứa nội dung có khả năng gây hại";
                return false;
            }
        }

        return true;
    }
}
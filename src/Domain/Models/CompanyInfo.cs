namespace Domain.Models;

/// <summary>
/// Normalized company information mapped from external APIs (e.g., OpenCorporates).
/// </summary>
public class CompanyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string JurisdictionCode { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string IncorporationDate { get; set; } = string.Empty;
}

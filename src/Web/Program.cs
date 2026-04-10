using Infrastructure;
using Serilog;

// Cấu hình Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

try
{
    Log.Information("Bắt đầu khởi động ứng dụng SalesMind AI...");
    
    var builder = WebApplication.CreateBuilder(args);

    // Thay thế logging mặc định bằng Serilog
    builder.Host.UseSerilog();

    // Add services to the container.
    var mvcBuilder = builder.Services.AddControllersWithViews();

    if (builder.Environment.IsDevelopment())
    {
        mvcBuilder.AddRazorRuntimeCompilation();
    }

    // Register Infrastructure services
    builder.Services.AddInfrastructure(builder.Configuration);

    var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllers();


    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Ứng dụng bị dừng đột ngột!");
}
finally
{
    Log.CloseAndFlush();
}

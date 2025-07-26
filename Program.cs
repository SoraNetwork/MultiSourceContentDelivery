using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;
using Polly;
using Polly.Extensions.Http;

namespace MultiSourceContentDelivery
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls("http://*:5010");
            // Add configuration
            builder.Services.Configure<NodeConfig>(
                builder.Configuration.GetSection("NodeConfig"));
            builder.Services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<NodeConfig>>().Value);

            // Add database context
            builder.Services.AddDbContext<FileInfoContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Add services
            builder.Services.AddHttpClient("NodeSync", client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("User-Agent", "MultiSourceContentDelivery-Node");
                })
                .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                .AddTransientHttpErrorPolicy(policy => policy
                    .WaitAndRetryAsync(3, retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (exception, timeSpan, retryCount, context) =>
                        {
                            Console.WriteLine($"正在重试，第 {retryCount} 次尝试，延迟 {timeSpan.TotalSeconds} 秒");
                        }))
                .AddTransientHttpErrorPolicy(policy => policy
                    .CircuitBreakerAsync(
                        handledEventsAllowedBeforeBreaking: 5,
                        durationOfBreak: TimeSpan.FromSeconds(30),
                        onBreak: (exception, duration) =>
                        {
                            Console.WriteLine($"断路器已开启，持续时间：{duration.TotalSeconds} 秒");
                        },
                        onReset: () =>
                        {
                            Console.WriteLine("断路器已重置");
                        }));

            builder.Services.AddSingleton<FileHashCalculatorService>();
            builder.Services.AddSingleton<DnsResolutionService>();
            builder.Services.AddScoped<FileStorageService>();
            builder.Services.AddHostedService<DirectoryScanService>();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                c.CustomSchemaIds(type =>
                {
                    if (type == typeof(System.IO.FileInfo))
                        return "SystemFileInfo";
                    return type.Name;
                });
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            // app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();

            using (var scope = app.Services.CreateScope())
            {
                var services = scope.ServiceProvider;
                try
                {
                    var context = services.GetRequiredService<FileInfoContext>();
                    context.Database.EnsureCreated(); // 确保数据库已创建
                    context.Database.Migrate(); // 应用所有待处理的迁移
                    Console.WriteLine("Database migration completed successfully.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString(), "An error occurred while migrating the database.");
                    // 根据需要，可以选择在这里停止应用程序或继续运行
                }
            }


            app.Run();
        }
    }
}

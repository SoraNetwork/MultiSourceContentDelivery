using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MultiSourceContentDelivery.DbContexts;
using MultiSourceContentDelivery.Models;
using MultiSourceContentDelivery.Services;

namespace MultiSourceContentDelivery
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add configuration
            builder.Services.Configure<NodeConfig>(
                builder.Configuration.GetSection("NodeConfig"));
            builder.Services.AddSingleton(sp =>
                sp.GetRequiredService<IOptions<NodeConfig>>().Value);

            // Add database context
            builder.Services.AddDbContext<FileInfoContext>(options =>
                options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Add services
            builder.Services.AddSingleton<DnsResolutionService>();
            builder.Services.AddSingleton<FileHashCalculatorService>();
            builder.Services.AddSingleton<FileStorageService>();
            builder.Services.AddHostedService<NodeCommunicationService>();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

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

            app.Run();
        }
    }
}

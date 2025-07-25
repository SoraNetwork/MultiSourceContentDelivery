using Microsoft.EntityFrameworkCore;

namespace MultiSourceContentDelivery.DbContexts;

public class FileInfoContext : DbContext
{
   public FileInfoContext(DbContextOptions<FileInfoContext> options) : base(options)
   {

   }

    public DbSet<Models.FileInfo> FileInfos { get; set; } = null!;
    public DbSet<Models.NodeInfo> Nodes { get; set; } = null!;
}

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Controllers;
using TobacoServer.Models.Jobs;

namespace TobacoServer.Models.DbContext
{
    public class AppDbContext : Microsoft.EntityFrameworkCore.DbContext
    {
        public DbSet<Delay> Delays { get; set; }
        public DbSet<TooManyPeopleAtStallFailure> TooManyPeopleAtStallFailures { get; set; }
        public DbSet<CashRegisterFailure> CashRegisterFailures { get; set; }
        public DbSet<Smoke> SmokeFailures { get; set; }
        public DbSet<LightFailure> LightFailures { get; set; }
        public DbSet<Crowd> Crowds { get; set; }
        public DbSet<ServiceNearCabinetFailure> ServiceNearCabinetFailures { get; set; }
        public DbSet<NoOneAtStallForTooLongFailure> NoOneAtStallForTooLongFailures { get; set; }
        public DbSet<HumanDetectionBeforeAndAfterShiftFailure> HumanDetectionBeforeAndAfterShiftFailures { get; set; }
        public DbSet<PhoneFailure> PhoneFailures { get; set; }
        public DbSet<PoseFailure> PoseFailures{ get; set; }
        public DbSet<MoppingFailure> MoppingFailures{ get; set; }
        public DbSet<CountingCashRegisterFailure> CountingCashRegisterFailures { get; set; }   
        public DbSet<ClothesControlFailure> ClothesControlFailures { get; set; }
        public DbSet<ConversionRegister> ConversionRegister { get; set; }
        public DbSet<ClearStallFailure> ClearStallFailures { get; set; }
        public DbSet<BottleFailure> BottleFailures { get; set; }
        public DbSet<InactiveSalesmanFailure> InactiveSalesmanFailures { get; set; }
        public DbSet<BadgeFailure> BadgeFailures { get; set; }
        public DbSet<AbandonedOpenCashRegisterFailure> AbandonedOpenCashRegisterFailures { get; set; }
        public DbSet<DefectImage> DefectImages { get; set; }


        public AppDbContext()
        {
            Database.Migrate();
            //_ = Database.EnsureCreated();
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            //builder.Entity<Defect>().UseTptMappingStrategy();
            builder.Entity<Role>(entity =>
            {
                entity.HasIndex(e => e.Name).IsUnique();
            });
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning));
            var connectionString = System.Configuration.ConfigurationManager.ConnectionStrings["Default"].ToString();
            _ = optionsBuilder.UseSqlServer(connectionString);
        }

    }
}

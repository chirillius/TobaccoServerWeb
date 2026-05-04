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
        public DbSet<PoseFailure> PoseFailures { get; set; }
        public DbSet<MoppingFailure> MoppingFailures { get; set; }
        public DbSet<CountingCashRegisterFailure> CountingCashRegisterFailures { get; set; }
        public DbSet<ClothesControlFailure> ClothesControlFailures { get; set; }
        public DbSet<ConversionRegister> ConversionRegister { get; set; }
        public DbSet<ConversionRegisterEvent> ConversionRegisterEvents { get; set; }
        public DbSet<ClearStallFailure> ClearStallFailures { get; set; }
        public DbSet<BottleFailure> BottleFailures { get; set; }
        public DbSet<InactiveSalesmanFailure> InactiveSalesmanFailures { get; set; }
        public DbSet<BadgeFailure> BadgeFailures { get; set; }
        public DbSet<AbandonedOpenCashRegisterFailure> AbandonedOpenCashRegisterFailures { get; set; }
        public DbSet<DefectImage> DefectImages { get; set; }


        public AppDbContext()
        {
            Database.Migrate();
            EnsureConversionRegisterEventsTable();
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

        private void EnsureConversionRegisterEventsTable()
        {
            const string sql = """
IF OBJECT_ID(N'[dbo].[ConversionRegisterEvents]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[ConversionRegisterEvents] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [LocalName] NVARCHAR(MAX) NOT NULL,
        [Name] NVARCHAR(MAX) NOT NULL,
        [DefectImageId] BIGINT NULL,
        [DateTime] DATETIME2 NOT NULL,
        [PeopleNumber] INT NOT NULL,
        [Verified] BIT NOT NULL CONSTRAINT [DF_ConversionRegisterEvents_Verified] DEFAULT(0),
        [CameraName] NVARCHAR(256) NULL,
        CONSTRAINT [PK_ConversionRegisterEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConversionRegisterEvents_DefectImages_DefectImageId]
            FOREIGN KEY ([DefectImageId]) REFERENCES [dbo].[DefectImages]([Id])
    );

    CREATE INDEX [IX_ConversionRegisterEvents_DateTime] ON [dbo].[ConversionRegisterEvents]([DateTime]);
    CREATE INDEX [IX_ConversionRegisterEvents_DefectImageId] ON [dbo].[ConversionRegisterEvents]([DefectImageId]);
END
""";

            Database.ExecuteSqlRaw(sql);
        }

    }
}

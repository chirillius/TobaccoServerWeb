using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using TobaccoEntities;
using TobaccoEntities.Models;
using TobacoServer.Models;
using TobacoServer.Models.DbContext;
using TobacoServer.Models.Services;
using TobacoServer.Services;
using TobacoServer.Services.ExceptionHandlers;
using TobacoServer.Services.Logging;



var builder = WebApplication.CreateBuilder(args);
var storePath = Path.Combine(Directory.GetCurrentDirectory(), "Configuration", "thisStore.json");

var storeManagerService = new CurrentStoreHandlingService();

var thisStore = storeManagerService.GetStore();
//if (!Path.Exists(storePath))
//{
//    var fs = File.Create(storePath);
//    fs.Dispose();
//    File.WriteAllText(storePath, JsonConvert.SerializeObject(new Store()));
//}
//var thisStore = JsonConvert.DeserializeObject<Store>(
//    File.ReadAllText(storePath));

//builder.Services.AddSingleton<Store>(thisStore);

builder.Logging.AddFile("log.txt");

// TO DO
builder.Services.AddDbContext<AppDbContext>();

//builder.Services.AddTransient<AppDbContext>();
builder.Services.AddTransient<AudioService>();
var videoService = new VideoCacheService();
builder.Services.AddSingleton(videoService);
builder.Services.AddSingleton(storeManagerService);
builder.Services.AddSingleton<StreamingHandlerService>();
builder.Services.AddSingleton<ILogger>(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger<FileLogger>());
builder.Services.AddScoped<DefectCutterService>();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();



// Add services to the container.
builder.Services.AddControllersWithViews();


var app = builder.Build();
app.UseExceptionHandler();

//// Configure the HTTP request pipeline.
//if (!app.Environment.IsDevelopment())
//{
//    _ = app.UseExceptionHandler("/Home/Error");
//    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
//}
_ = app.UseHsts();
app.UseCors(options =>
{
    _ = options.AllowAnyMethod();
    _ = options.AllowAnyHeader();
    _ = options.SetIsOriginAllowed(x => true);
    _ = options.AllowCredentials();
});

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();


app.UseAuthorization();

app.UseHttpsRedirection();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");


PeriodicalTasksConfigurator periodalTasksConfigurator = null;
app.Lifetime.ApplicationStarted.Register(async () =>
{
    periodalTasksConfigurator = new PeriodicalTasksConfigurator(app.Services, app.Logger);
    await periodalTasksConfigurator.ConfigurePeriodicalTasksAsync();
});

//app.Lifetime.ApplicationStopped.Register(async () =>
//{
//    if (periodalTasksConfigurator is not null)
//    {
//        periodalTasksConfigurator.Dispose();
//    }
//});




//Создание папок 
var videosDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["VideosDirectory"]);
var tempDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["TempDirectory"]);
var archiveDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["ArchivesDirectory"]);
var imagesDirectory = Path.Combine(Directory.GetCurrentDirectory(), System.Configuration.ConfigurationManager.AppSettings["ImagesDirectory"]);

Directory.CreateDirectory(videosDirectory);
Directory.CreateDirectory(tempDirectory);
Directory.CreateDirectory(archiveDirectory);

//Начинает отслеживать содержимое папок

ArchiveHelper.Activate(videosDirectory, archiveDirectory, tempDirectory, imagesDirectory);

var context = new AppDbContext();
var videoRecorder = new VideoAnalyzerService(videosDirectory, app.Logger, videoService, context);

foreach (var camera in thisStore.Cameras)
{
    videoRecorder.AnalyzeStream(camera.Name, camera.Address);
}

//using (var scope = app.Services.CreateScope())  
//{
//    var defectCutterService = scope.ServiceProvider.GetService<DefectCutterService>();
//    await defectCutterService.ScheduleRuns();

app.Run();
//}


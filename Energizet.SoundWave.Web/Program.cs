using Energizet.SoundWave.Web.Controllers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(x => x.AddDefaultPolicy(policy =>
{
	policy.AllowAnyHeader();
	policy.AllowAnyMethod();
	policy.AllowAnyOrigin();
}));

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();

builder.Services.AddSingleton<BackgroundWorker>();
builder.Services.AddHostedService<BackgroundWorker>(provider =>
	provider.GetRequiredService<BackgroundWorker>());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
	app.UseHsts();
}

app.UseDeveloperExceptionPage();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Main}/{action=Index}/{id?}");

app.MapHub<SignalRHub>(nameof(SignalRHub));

app.Run();
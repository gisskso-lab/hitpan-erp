using HqaS4Fixture.Components;
using MudBlazor.Services;

// HQA 관문 ① 직접 실측용 시험 앱 — 가짜 데이터 · 인증 없음 · 서버 저장 0 · DB 0.
// 백오피스(src/HitPan.Backoffice/Program.cs)와 같은 렌더 꼴: Razor 컴포넌트 + InteractiveServer · 미리 그리기 기본값(켬).
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddMudServices();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    // 꾸러미 정적 파일(_content/MudBlazor/*)은 개발 환경에서만 NuGet 캐시에서 바로 나온다.
    // Production 으로 띄우면 MudBlazor.min.css · MudBlazor.min.js 가 404 → 목록이 열리지 않는다(README 「기동」 참조).
    app.Logger.LogWarning(
        "환경이 {Env} 입니다. ASPNETCORE_ENVIRONMENT=Development 로 띄우십시오 — 아니면 MudBlazor 정적 파일이 404 납니다.",
        app.Environment.EnvironmentName);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

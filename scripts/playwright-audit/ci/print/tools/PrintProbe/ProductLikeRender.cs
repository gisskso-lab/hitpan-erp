// W-G1-51 갈래 Z 7차(C3 · CTO 재판정 §5-2) — 「제품 렌더 경로(같은 라이브러리 · 같은 글꼴)」 로 PDF 를 만든다 (시험 쪽 도구 · 제품 코드 0)
//   같은 라이브러리 = QuestPDF 2026.2.4(제품 src/HitPan.Application/HitPan.Application.csproj:26 와 같은 판 · 이 csproj 에 이미 있음)
//   같은 글꼴      = DefaultTextStyle FontFamily("Malgun Gothic")(제품 PdfRenderService.cs:198 · :2747 와 같은 이름 · 글꼴 파일을 싣지 않음
//                    = 그 PC · 그 실행기에 있는 글꼴을 제품과 똑같이 찾는다 → CI 리눅스에서 한글이 안 뽑히면 제품 PDF 도 같은 겹에서 막힌다)
//   같은 그리기 꼴 = 양식지 그림 page.Background().Layers(Layer().Image().FitArea() + PrimaryLayer().Extend())(PdfRenderService.cs:2474~2483)
//                    · 좌표 글자 Layers(PrimaryLayer().Height(297mm) + Layer().TranslateX/Y(mm).Width(mm).Text().FontSize(pt))(:2929~:2995)
//   🔴 제품 함수(PdfRenderService) 자체를 부르지 않는다 — src 참조 = 장치 면을 제품 빌드까지 넓힘(7차 범위 밖). 그 차이는 명세서 §1-7 C3 칸에 적는다.
//   입력 = 그리기 명세 JSON(chain.js 가 fixture.js 의 양성 대조 정의에서 만든다) · 판정 0.
using System.Text.Json.Nodes;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PrintProbe;

public static class ProductLikeRender
{
    public const string ProductFont = "Malgun Gothic";

    /// <summary>명세: { "pages": [ { "bg": "png 경로" | null, "table": ["No","품명",…] | null, "tableY": mm, "texts": [ { "x","y","w","pt","align","t" } ] } ] }</summary>
    public static byte[] Render(string specPath)
    {
        QuestPDF.Settings.License = LicenseType.Community; // 제품 PdfRenderService.cs:31 과 같은 설정 1줄만
        var spec = JsonNode.Parse(File.ReadAllText(specPath))!.AsObject();
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(specPath)) ?? ".";
        var pages = spec["pages"]!.AsArray();
        return Document.Create(doc =>
        {
            foreach (var pn in pages)
            {
                var p = pn!.AsObject();
                var bgRel = p["bg"]?.GetValue<string>();
                var bg = string.IsNullOrEmpty(bgRel) ? null : File.ReadAllBytes(Path.Combine(baseDir, bgRel));
                var table = p["table"]?.AsArray().Select(n => n!.GetValue<string>()).ToArray();
                var tableY = (float)(p["tableY"]?.GetValue<double>() ?? 60);
                var texts = p["texts"]?.AsArray() ?? new JsonArray();
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(t => t.FontFamily(ProductFont).FontSize(10));
                    if (bg is not null)
                    {
                        page.Background().Layers(layers =>
                        {
                            layers.Layer().Image(bg).FitArea();
                            layers.PrimaryLayer().Extend();
                        });
                    }
                    page.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Height(297, Unit.Millimetre);
                        if (table is { Length: > 0 })
                        {
                            layers.Layer().TranslateX(15, Unit.Millimetre).TranslateY(tableY, Unit.Millimetre).Width(180, Unit.Millimetre).Table(t =>
                            {
                                t.ColumnsDefinition(c => { foreach (var _ in table) c.RelativeColumn(); });
                                t.Header(h => { foreach (var name in table) h.Cell().Text(name).FontSize(9); });
                            });
                        }
                        foreach (var tn in texts)
                        {
                            var o = tn!.AsObject();
                            var w = (float)(o["w"]?.GetValue<double>() ?? 30);
                            var cell = layers.Layer()
                                .TranslateX((float)o["x"]!.GetValue<double>(), Unit.Millimetre)
                                .TranslateY((float)o["y"]!.GetValue<double>(), Unit.Millimetre)
                                .Width(w > 0 ? w : 30, Unit.Millimetre);
                            var aligned = (o["align"]?.GetValue<string>()) switch
                            {
                                "right" => cell.AlignRight(),
                                "center" => cell.AlignCenter(),
                                _ => cell,
                            };
                            var pt = (float)(o["pt"]?.GetValue<double>() ?? 10);
                            aligned.Text(o["t"]!.GetValue<string>()).FontSize(pt > 0 ? pt : 10);
                        }
                    });
                });
            }
        }).GeneratePdf();
    }
}

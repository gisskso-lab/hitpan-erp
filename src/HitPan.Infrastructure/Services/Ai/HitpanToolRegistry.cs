using System.Text.Json;

namespace HitPan.Application.Services.Ai;

// ─────────────────────────────────────────────────────────────────
// 히트판 Tool 레지스트리 — 등록된 모든 Tool 을 모아 클로드에 노출하고 이름으로 찾는다.
//
// 사장님 비전(FSD): 수동 버튼(기존 기능) 하나하나를 Tool 로 등록하면, 챗봇이 할 줄 아는 일이
//   그만큼 늘어난다. 새 Tool 클래스 1개 추가 → DI 등록 → 끝(엔진·프롬프트 수정 0).
// ─────────────────────────────────────────────────────────────────

/// <summary>등록된 Tool 들의 카탈로그 + 이름 조회.</summary>
public interface IHitpanToolRegistry
{
    /// <summary>클로드 tools 파라미터에 실을 전체 Tool 정의(JSON 배열).</summary>
    JsonElement BuildToolCatalog();

    /// <summary>이름으로 Tool 찾기(없으면 null).</summary>
    IHitpanTool? Find(string name);
}

public sealed class HitpanToolRegistry : IHitpanToolRegistry
{
    private readonly Dictionary<string, IHitpanTool> _tools;
    private readonly JsonElement _catalog;

    public HitpanToolRegistry(IEnumerable<IHitpanTool> tools)
    {
        _tools = new Dictionary<string, IHitpanTool>(StringComparer.OrdinalIgnoreCase);
        var defs = new List<object>();
        foreach (var tool in tools)
        {
            if (_tools.ContainsKey(tool.Name))
            {
                continue; // 중복 이름 방지(먼저 등록된 것 유지).
            }
            _tools[tool.Name] = tool;
            defs.Add(new
            {
                name = tool.Name,
                description = tool.Description,
                input_schema = tool.InputSchema
            });
        }
        _catalog = JsonSerializer.SerializeToElement(defs);
    }

    public JsonElement BuildToolCatalog() => _catalog;

    public IHitpanTool? Find(string name)
        => _tools.TryGetValue(name, out var t) ? t : null;
}

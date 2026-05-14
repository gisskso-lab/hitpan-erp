using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HitPan.Analyzers;

/// <summary>
/// 헌법 #15: 빈 catch 블록 금지 — 최소한 _logger.LogWarning(ex, ...) 한 줄 필요.
/// silent swallow는 운영 사고 추적 불가.
///
/// 검출 패턴:
/// 1. catch { }  (완전 빈 블록)
/// 2. catch (Exception) { }  (빈 블록 + 빈 본문)
/// 3. catch { /* 주석만 */ }  (실행문 0개)
///
/// Severity: Warning (헌법 #19 errors 0 + warnings 0 정책상 사실상 Error 효과,
/// 단 도입 시점 기존 위반 수 미지수이므로 Warning으로 시작 후 Error 승격).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EmptyCatchAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HP0015";

    private static readonly LocalizableString Title = "빈 catch 블록 금지 (헌법 #15)";
    private static readonly LocalizableString MessageFormat =
        "catch 블록이 비어 있습니다. 최소한 _logger.LogWarning(ex, ...) 한 줄을 추가하세요 (헌법 #15).";
    private static readonly LocalizableString Description =
        "silent swallow는 운영 사고 추적을 불가능하게 만든다. 모든 catch는 로깅 또는 재throw 필요.";
    private const string Category = "HitPanConstitution";

    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: Description,
        helpLinkUri: "https://github.com/gisskso-lab/hitpan-erp/blob/develop/CLAUDE.md#절대-원칙-위반-시-즉시-반려");

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeCatchClause, SyntaxKind.CatchClause);
    }

    private static void AnalyzeCatchClause(SyntaxNodeAnalysisContext context)
    {
        var catchClause = (CatchClauseSyntax)context.Node;
        var block = catchClause.Block;
        if (block == null) return;

        // 실행문 0개 → 빈 catch. 주석만 있어도 Statements는 비어있다.
        if (!block.Statements.Any())
        {
            var diag = Diagnostic.Create(Rule, catchClause.CatchKeyword.GetLocation());
            context.ReportDiagnostic(diag);
        }
    }
}

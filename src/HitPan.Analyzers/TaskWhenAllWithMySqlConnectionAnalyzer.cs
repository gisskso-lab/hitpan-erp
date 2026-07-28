using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace HitPan.Analyzers;

/// <summary>
/// 헌법 #16: MySqlConnection + Task.WhenAll 조합 금지.
/// MySqlConnection은 thread-safe가 아님. 병렬 쿼리 시 "connection in use" 에러.
/// 다중 KPI는 UNION ALL 단일 쿼리 또는 QueryMultipleAsync 사용.
///
/// 검출 패턴(보수적 휴리스틱):
/// - 같은 메서드 본문 안에 Task.WhenAll(...) 호출이 있고
/// - 그 메서드 본문에 MySqlConnection 타입 식별자가 등장한다.
///
/// Severity: Warning (헌법 #19 정책상 사실상 Error 효과).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class TaskWhenAllWithMySqlConnectionAnalyzer : DiagnosticAnalyzer
{
    public const string DiagnosticId = "HP0016";

    private static readonly LocalizableString Title = "MySqlConnection + Task.WhenAll 금지 (헌법 #16)";
    private static readonly LocalizableString MessageFormat =
        "동일 메서드에서 MySqlConnection과 Task.WhenAll이 함께 사용되었습니다. " +
        "MySqlConnection은 thread-safe가 아닙니다. UNION ALL 또는 QueryMultipleAsync로 직렬화하세요 (헌법 #16).";
    private static readonly LocalizableString Description =
        "MySqlConnection을 여러 Task에서 공유하면 'connection in use' 런타임 오류 발생.";
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
        context.RegisterSyntaxNodeAction(AnalyzeMethod, SyntaxKind.MethodDeclaration);
    }

    private static void AnalyzeMethod(SyntaxNodeAnalysisContext context)
    {
        var method = (MethodDeclarationSyntax)context.Node;
        if (method.Body == null && method.ExpressionBody == null) return;

        SyntaxNode body = (SyntaxNode?)method.Body ?? method.ExpressionBody!;

        // Task.WhenAll 호출 찾기
        var whenAllCalls = body.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(inv => IsTaskWhenAll(inv))
            .ToList();
        if (whenAllCalls.Count == 0) return;

        // 메서드 본문에 MySqlConnection 등장 여부
        var hasMySqlConnection = body.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => id.Identifier.ValueText == "MySqlConnection");
        if (!hasMySqlConnection) return;

        foreach (var inv in whenAllCalls)
        {
            var diag = Diagnostic.Create(Rule, inv.GetLocation());
            context.ReportDiagnostic(diag);
        }
    }

    private static bool IsTaskWhenAll(InvocationExpressionSyntax inv)
    {
        if (inv.Expression is MemberAccessExpressionSyntax mae)
        {
            if (mae.Name.Identifier.ValueText != "WhenAll") return false;
            // Task.WhenAll, System.Threading.Tasks.Task.WhenAll
            return mae.Expression.ToString().EndsWith("Task");
        }
        return false;
    }
}

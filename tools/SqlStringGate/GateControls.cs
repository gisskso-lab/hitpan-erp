namespace SqlStringGate;

internal static class GateControls
{
    public static readonly (string Source, RuleId Rule)[] Negative =
    {
        ("var s = @\"SELECT 1 --\n, @p\";", RuleId.F1),
        ("var s = $@\"SELECT {x} --\n, @p\";", RuleId.F1),
        ("var s = \"\"\"\n  SELECT 1 --\n  , @p\n  \"\"\";", RuleId.F1),
        ("var s = $\"\"\"\n  SELECT {x} --\n  , @p\n  \"\"\";", RuleId.F1),
        ("var s = \"SELECT 1 --\\n, @p\";", RuleId.F1),
        ("var s = \"SELECT 1 --\\n\" +\n  \", @p\";", RuleId.F1),
        ("var s = \"SELECT 1 --\" + \"\\n\" + \", @p\";", RuleId.F1),
        ("var s = $\"SELECT {x} --\\n, @p\";", RuleId.F1),
        ("var s = \"SELECT 1 --\" + x + \", @p\";", RuleId.F2),
        ("const string A = \"SELECT 1 --\\n\"; var s = A + B;", RuleId.F3),
        ("var s = $\"SELECT 1 --{x}\\n, @p\";", RuleId.F4),
        ("var s = $@\"SELECT 1 --\n{x}\n, @p\";", RuleId.F1),
        ("var s = @\"SELECT 1 --\n--\n--\n, @p\";", RuleId.F1),
        ("var s = @\"x --\n, @p\";", RuleId.F1),
        ("var s = @\"x --\n, @Name_1\";", RuleId.F1),
    };

    public static readonly string[] Positive =
    {
        "var s = @\"SELECT 1 -- note\n, @p\";",
        "var s = @\"SELECT 1 --\n-- note\n, @p\";",
        "var s = @\"SELECT 1 --\n\n, @p\";",
        "var s = @\"plain --\nnext text\";",
        "// ------\nint i = 0;",
        "i--\n;x();",
        "var s = @\"SELECT '--\n' , @p\";",
        "var s = @\"/* --\n */ SELECT @p\";",
        "var s = @\"SELECT 1 --\n--\n, @p\";",
        "var s = \"SELECT 1 --\\r\\n, @p\";",
    };

    public static readonly string[] Broken =
    {
        "var s = \"SELECT 1\n\";",
        "/* SELECT 1",
        "var s = $@\"SELECT {x",
    };
}

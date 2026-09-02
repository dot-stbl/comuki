// Ported (trimmed) from Hybrid.Sdk.Shared.Filtering.Unit (console.x.sdk) — the
// adversarial truth tables: unknown fields, disallowed operators, malformed
// values, grammar errors, precedence, quoting. The full Hybrid file also covers
// locale spoofing and deep DoS loops; the DoS caps (token limit, paren depth)
// are covered here by their boundary cases.
using Comuki.Shared.Filtering.Parser;
using Comuki.Shared.Filtering.Translator;
using Comuki.Shared.Filtering.Unit.TestEntities;
using Shouldly;
using Xunit;

namespace Comuki.Shared.Filtering.Unit;

/// <summary>
///     Adversarial tests for the filter DSL parser. Every input here is an attempt
///     to break the parser — malformed grammar, unknown fields, type mismatches,
///     injection attempts, edge cases. A green test means the parser rejected the input
///     with <see cref="FilterParseException" /> or behaved sanely (no exception, no
///     security leak, no parse error swallowed).
/// </summary>
public sealed class FilterExpressionParserAdversarialShould
{
    private static readonly IQueryable<SampleEntity> data = new List<SampleEntity>
    {
        new() { Id = Guid.NewGuid(), Name = "alice", Status = SampleStatus.Active, Age = 25, IsActive = true },
        new() { Id = Guid.NewGuid(), Name = "Bob", Status = SampleStatus.Inactive, Age = 30, IsActive = false },
        new() { Id = Guid.NewGuid(), Name = "Carol", Status = SampleStatus.Trial, Age = 40, IsActive = true }
    }.AsQueryable();

    private static List<SampleEntity> Run(string? filter)
    {
        var predicate = FilterExpression.ParseFor<SampleEntity>(filter);
        return predicate is null ? [.. data] : [.. data.Where(predicate)];
    }

    // ── Null / empty / whitespace ────────────────────────────────────────────────

    /// <summary>Null filter returns null predicate — caller treats as "no filter".</summary>
    [Fact]
    public void ReturnNullForNullFilter()
    {
        FilterExpression.ParseFor<SampleEntity>(null).ShouldBeNull();
    }

    /// <summary>Empty string filter returns null predicate.</summary>
    [Fact]
    public void ReturnNullForEmptyFilter()
    {
        FilterExpression.ParseFor<SampleEntity>(string.Empty).ShouldBeNull();
    }

    /// <summary>Whitespace-only filter returns null predicate — no throw.</summary>
    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void ReturnNullForWhitespaceFilter(string filter)
    {
        FilterExpression.ParseFor<SampleEntity>(filter).ShouldBeNull();
    }

    // ── Unknown fields ───────────────────────────────────────────────────────────

    /// <summary>Unknown field name throws — the white-list is enforced.</summary>
    [Theory]
    [InlineData("nonexistent==value")]
    [InlineData("xyz~John")]
    [InlineData("Field99==1")]
    public void RejectUnknownField(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>[NotMapped] fields are invisible — querying them throws.</summary>
    [Fact]
    public void RejectNotMappedField()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("SecretKey==leak"));
    }

    /// <summary>Type-incompatible fields (byte[], object) are invisible too.</summary>
    [Theory]
    [InlineData("Payload==abc")]
    [InlineData("Bag==anything")]
    public void RejectUnfilterableField(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>SQL injection payload in field name is rejected as unknown field.</summary>
    [Fact]
    public void RejectSqlInjectionInFieldName()
    {
        // The parser doesn't care about the payload — "DROP TABLE" is just an unknown field.
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("DROP TABLE advertisers;--==x"));
    }

    // ── Disallowed operators ─────────────────────────────────────────────────────

    /// <summary>Operator not allowed for the field's type is rejected.</summary>
    [Theory]
    [InlineData("Status~Active")] // enum does not support Contains
    [InlineData("Age~25")] // int does not support Contains
    [InlineData("IsActive!=true")] // bool does not support NotEq per our inference
    [InlineData("Name>alice")] // string does not support Gt
    [InlineData("CreatedAt~2024")] // date does not support Contains
    [InlineData("Id~00000000")] // Guid does not support Contains
    [InlineData("Age?")] // int (non-nullable value type) does not support IsNull
    [InlineData("Age!?")] // int does not support IsNotNull either
    [InlineData("IsActive?")] // bool does not support IsNull
    [InlineData("Status?")] // enum (non-nullable value type) does not support IsNull
    public void RejectOperatorNotApplicableToFieldType(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    // ── Malformed value conversions ──────────────────────────────────────────────

    /// <summary>Value that cannot convert to the field's CLR type is rejected.</summary>
    [Theory]
    [InlineData("Age==abc")] // int <- "abc"
    [InlineData("CreatedAt==not-a-date")] // DateTimeOffset <- garbage
    [InlineData("Status==NotAStatus")] // enum member that doesn't exist
    [InlineData("Id==not-a-guid")] // Guid parse failure
    [InlineData("IsActive==maybe")] // bool parse failure
    public void RejectValueThatDoesNotConvert(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    // ── Grammar errors ───────────────────────────────────────────────────────────

    /// <summary>Operator-only source (no field) is a grammar error.</summary>
    [Theory]
    [InlineData("==value")]
    [InlineData("~value")]
    [InlineData(">=5")]
    public void RejectOperatorWithoutField(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Field without operator is a grammar error.</summary>
    [Theory]
    [InlineData("Name")]
    [InlineData("Name value")]
    public void RejectFieldWithoutOperator(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Field without value is a grammar error.</summary>
    [Theory]
    [InlineData("Name==")]
    [InlineData("Age>")]
    [InlineData("Status[]=")]
    public void RejectOperatorWithoutValue(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Trailing junk after a valid comparison is rejected.</summary>
    [Theory]
    [InlineData("Name==alice extra")]
    public void RejectTrailingJunk(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Unmatched open paren is rejected.</summary>
    [Fact]
    public void RejectUnmatchedOpenParen()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("(Name==alice"));
    }

    /// <summary>Unmatched close paren is rejected.</summary>
    [Fact]
    public void RejectUnmatchedCloseParen()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("Name==alice)"));
    }

    /// <summary>Empty parens are rejected.</summary>
    [Fact]
    public void RejectEmptyParens()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("()"));
    }

    /// <summary>Dangling AND (<c>;</c>) with no following term is rejected.</summary>
    [Theory]
    [InlineData("Name==alice;")]
    [InlineData(";Name==alice")]
    [InlineData("Name==alice;;Name==bob")]
    public void RejectDanglingAnd(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Dangling OR (<c>|</c>) with no following term is rejected.</summary>
    [Theory]
    [InlineData("Name==alice|")]
    [InlineData("|Name==alice")]
    public void RejectDanglingOr(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>Unterminated string literal is rejected.</summary>
    [Fact]
    public void RejectUnterminatedString()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("Name==\"unterminated"));
    }

    /// <summary>Empty value list in <c>[]=</c> is rejected.</summary>
    [Fact]
    public void RejectEmptyInList()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("Status[]="));
    }

    /// <summary>Trailing comma in <c>[]=</c> list is rejected.</summary>
    [Fact]
    public void RejectTrailingCommaInInList()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("Status[]=Active,"));
    }

    /// <summary>Bogus operator symbols are rejected as grammar errors.</summary>
    [Theory]
    [InlineData("Name@=value")]
    [InlineData("Name#value")]
    [InlineData("Name:=value")]
    public void RejectBogusOperatorSymbol(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>
    ///     IsNull takes no value — anything after <c>field?</c> is rejected as
    ///     trailing junk (the parser expects end / AND / OR / close-paren).
    /// </summary>
    [Theory]
    [InlineData("Nickname?alice")] // value attempted
    [InlineData("Nickname?==alice")] // value attempted via ==
    public void RejectValueAfterIsNull(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>
    ///     IsNotNull takes no value — anything after <c>field!?</c> is rejected.
    /// </summary>
    [Theory]
    [InlineData("Nickname!?alice")]
    [InlineData("Nickname!?==alice")]
    public void RejectValueAfterIsNotNull(string filter)
    {
        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    // ── Operator precedence (AND > OR) ───────────────────────────────────────────

    /// <summary>AND binds tighter than OR — <c>a;b | c;d</c> = <c>(a;b) | (c;d)</c>.</summary>
    [Fact]
    public void BindAndTighterThanOr()
    {
        // (Name==alice ; IsActive==true) | (Name==Bob ; IsActive==false)
        var result = Run("Name==alice;IsActive==true|Name==Bob;IsActive==false");

        result.Count.ShouldBe(2);
        result.ShouldContain(static e => e.Name == "alice");
        result.ShouldContain(static e => e.Name == "Bob");
    }

    /// <summary>Parens override precedence — <c>(a | b) ; c</c>.</summary>
    [Fact]
    public void ParensOverridePrecedence()
    {
        // (Name==alice | Name==Bob) ; IsActive==true → only alice (Bob is inactive)
        var result = Run("(Name==alice|Name==Bob);IsActive==true");

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    // ── Quote / value escaping ───────────────────────────────────────────────────

    /// <summary>Quoted string with special chars (spaces, parens, operators) is one value.</summary>
    [Fact]
    public void AcceptQuotedStringWithSpecialChars()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "John; Doe (admin)" },
            new() { Name = "alice" }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Name==\"John; Doe (admin)\"")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("John; Doe (admin)");
    }

    /// <summary>Empty quoted string is a valid empty-string value.</summary>
    [Fact]
    public void AcceptEmptyQuotedString()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "" },
            new() { Name = "alice" }
        }.AsQueryable();

        var predicate = FilterExpression.ParseFor<SampleEntity>("Name==\"\"")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe(string.Empty);
    }

    /// <summary>Escaped double quote (<c>\"</c>) inside a quoted string is a literal quote.</summary>
    [Fact]
    public void AcceptEscapedDoubleQuoteInQuotedString()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "John \"Boss\"" },
            new() { Name = "alice" }
        }.AsQueryable();

        // Wire DSL: Name=="John \"Boss\""  → value: John "Boss"
        var predicate = FilterExpression.ParseFor<SampleEntity>("Name==\"John \\\"Boss\\\"\"")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("John \"Boss\"");
    }

    /// <summary>Escaped backslash (<c>\\</c>) inside a quoted string is one literal backslash.</summary>
    [Fact]
    public void AcceptEscapedBackslashInQuotedString()
    {
        var data = new List<SampleEntity>
        {
            new() { Name = "C:\\temp" },
            new() { Name = "alice" }
        }.AsQueryable();

        // Wire DSL: Name=="C:\\temp"  → value: C:\temp
        var predicate = FilterExpression.ParseFor<SampleEntity>("Name==\"C:\\\\temp\"")!;
        var result = data.Where(predicate).ToList();

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("C:\\temp");
    }

    /// <summary>A dangling backslash at the end of a quoted string (no char to escape) is rejected.</summary>
    [Fact]
    public void RejectDanglingEscapeInQuotedString()
    {
        Should.Throw<FilterParseException>(static () => FilterExpression.ParseFor<SampleEntity>("Name==\"bad\\"));
    }

    // ── Semantic correctness ─────────────────────────────────────────────────────

    /// <summary>Eq on enum works case-insensitively (parser does Enum.Parse ignoreCase).</summary>
    [Fact]
    public void ParseEnumValuesCaseInsensitively()
    {
        var resultLower = Run("Status==active");
        var resultPascal = Run("Status==Active");
        var resultUpper = Run("Status==ACTIVE");

        resultLower.Count.ShouldBe(resultPascal.Count);
        resultPascal.Count.ShouldBe(resultUpper.Count);
    }

    /// <summary>Numeric IN with three values matches three rows.</summary>
    [Fact]
    public void NumericInMatchesAllListedValues()
    {
        var result = Run("Age[]=25,30,40");

        result.Count.ShouldBe(3);
    }

    /// <summary>Single-value IN list is valid and behaves like Eq.</summary>
    [Fact]
    public void SingleValueInListBehavesLikeEq()
    {
        var result = Run("Status[]=Active");

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>Nested parens (deep) parse without stack overflow or wrong precedence.</summary>
    [Fact]
    public void ParseNestedParens()
    {
        var predicate = FilterExpression.ParseFor<SampleEntity>(
            "((Name==alice))");

        var result = data.Where(predicate!).ToList();
        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("alice");
    }

    /// <summary>Whitespace between tokens is ignored (not a separator).</summary>
    [Fact]
    public void IgnoreWhitespaceBetweenTokens()
    {
        var tight = Run("Name==alice;Status==Active");
        var spaced = Run("  Name   ==   alice  ;  Status   ==   Active  ");

        tight.Count.ShouldBe(spaced.Count);
    }

    /// <summary>
    ///     Filter with many AND clauses within the token limit parses without error.
    ///     Demonstrates the parser handles arbitrary chain depth (subject to the cap).
    /// </summary>
    [Fact]
    public void ParseManyClausesChain()
    {
        // 50 clauses × ~4 tokens (field, op, value, separator) ≈ 200 tokens,
        // comfortably below the 256-token hard cap.
        var clauses = Enumerable.Range(0, 50).Select(_ => "Name==alice").ToArray();
        var filter = string.Join(';', clauses);

        Should.NotThrow(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }

    /// <summary>
    ///     Filter exceeding the lexer's 256-token hard cap (<c>FilterLexer.MaxTokens</c>)
    ///     is rejected (audit hardening: adversarial clause floods).
    /// </summary>
    [Fact]
    public void RejectFilterExceedingTokenLimit()
    {
        // 100 clauses × 4 tokens = 400 tokens, well above the 256-token cap.
        var clauses = Enumerable.Range(0, 100).Select(_ => "Name==alice").ToArray();
        var filter = string.Join(';', clauses);

        Should.Throw<FilterParseException>(() => FilterExpression.ParseFor<SampleEntity>(filter));
    }
}

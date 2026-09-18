using Xunit;
using EasyAssertions;
using static DressSharp.UnitTests.EmitterTestHarness;

namespace DressSharp.UnitTests;

/// <summary>
/// Formatting may move source, but it may never lose any. These cover the two ways it did.
/// </summary>
public class SourcePreservationTests
{
    [Fact]
    public void Wrapped_items_survive_a_trailing_line_comment()
    {
        // The gap before an item is rendered from the previous item's trailing comment. Styles that
        // put the item on its own line supply no whitespace after that comment, so the item used to
        // be written into it.
        Format(
                """
                record R(
                    string First,   // first
                    string Second,  // second
                    string Third);
                """,
                ("max_line_length", "180"),
                ("csharp_indent_block_contents", "true"),
                ("dress_parameters_layout", "auto"))
            .ShouldBe(
                """
                record R(
                    string First,
                    // first
                    string Second,
                    // second
                    string Third);
                """);
    }

    [Fact]
    public void Xml_element_layout_keeps_disabled_text_and_its_directives()
    {
        const string source = """
            class C
            {
            #if false
                /// <summary>
                /// Hidden.
                /// </summary>
                void Hidden()
                {
                }
            #endif

                /// <summary>
                /// Kept.
                /// </summary>
                public int Value => 1;
            }
            """;

        // The rewrite flattens the whole trivia list to text and parses it back, and a parse of
        // leading trivia stops at the first thing that is not trivia. Nothing in the disabled
        // region is trivia to a parser with no preprocessor symbols.
        Format(source, ("dress_xml_element_layout", "single_line")).ShouldBe(source);
    }
}

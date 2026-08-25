# Rule reference: catalog version 1

Every rule is independently selected. Missing values and `unset` make no change. Keys ending in `_layout` accept `always_single`, `auto`, or `always_multi`; `auto` uses `max_line_length` (Default: `180`).

## DressSharp rules

| Keys | Accepted values | Owned syntax |
| --- | --- | --- |
| `dress_method_body`, `dress_constructor_body`, `dress_operator_body`, `dress_property_body`, `dress_indexer_body`, `dress_accessor_body`, `dress_lambda_body` | `block`, `expression` | Corresponding declarations or lambdas |
| `dress_namespace_style` | `file_scoped`, `block_scoped` | Namespace declarations |
| `dress_embedded_statement_placement` | `same_line`, `next_line` | Embedded statements of `if`/`else`, `while`, `do`, `for`, `foreach`, `using`, `lock`, and `fixed` |
| `dress_embedded_statement_braces` | `compact`, `balanced`, `always` | Brace-optional embedded statements; `balanced` makes each `if`/`else` chain uniform |
| `dress_braces_for_multiline_statement_header` | `true`, `false` | Adds braces when the owning statement header is multiline |
| `dress_arguments_layout`, `dress_parameters_layout`, `dress_initializers_layout`, `dress_collection_expressions_layout`, `dress_base_type_lists_layout`, `dress_constraint_clauses_layout`, `dress_member_access_chains_layout`, `dress_binary_expressions_layout`, `dress_conditional_expressions_layout`, `dress_query_clauses_layout`, `dress_attributes_layout` | `always_single`, `auto`, `always_multi` | Named syntax shape |
| `dress_blank_lines_around_namespaces`, `dress_blank_lines_around_types`, `dress_blank_lines_between_members`, `dress_blank_lines_between_using_groups`, `dress_blank_lines_between_member_categories`, `dress_max_consecutive_blank_lines` | Non-negative integer | Blank-line layout |
| `dress_line_comment_spacing`, `dress_block_comment_spacing` | `none`, `single` | Comment interior spacing |
| `dress_attached_comment_placement` | `same_line`, `own_line`, `auto` | Attached comments |
| `dress_xml_comment_placement` | `attached`, `separated` | XML documentation comments |
| `dress_xml_element_layout` | `single_line`, `multi_line` | Plain-text `///` XML elements with simple names |
| `dress_global_using_order` | `first`, `last`, `mixed` | Global using placement |
| `dress_using_kind_order` | Permutation of `ordinary,static,alias` | Using directives |
| `dress_object_initializer_indentation`, `dress_collection_initializer_indentation`, `dress_array_initializer_indentation`, `dress_with_initializer_indentation`, `dress_collection_expression_indentation` | `indented`, `not_indented` | Named initializer |

`dress_xml_element_layout` preserves nested XML elements and `/** */` documentation comments unchanged.

Embedded-statement placement applies to braced and unbraced bodies. `same_line` uses one space; `next_line` uses one configured newline and computed indentation. Comments and directives prevent rewriting their boundary, and `else if` remains a chain continuation. Placement overrides `csharp_new_line_before_open_brace` for these bodies.

`compact` uses braces only for planned multiline bodies or multiline headers when enabled. `balanced` applies the same test, then makes an entire `if`/`else if`/`else` chain uniform. `always` braces every supported body. Planned layout includes wrapping, but excludes the body's outer placement and braces. Empty bodies become `;` under `compact`/`balanced` and `{ }` under `always`, subject to syntax-safety constraints.

## Standard EditorConfig rules

DressSharp supports `charset`, `end_of_line`, `insert_final_newline`, `trim_trailing_whitespace`, `indent_style`, `indent_size`, `tab_width`, and `max_line_length`, plus these C#/.NET keys:

- `csharp_new_line_before_open_brace`, `csharp_new_line_before_else`, `csharp_new_line_before_catch`, `csharp_new_line_before_finally`, `csharp_new_line_before_members_in_object_initializers`, `csharp_new_line_before_members_in_anonymous_types`, `csharp_new_line_between_query_expression_clauses`
- `csharp_indent_switch_labels`, `csharp_indent_case_contents`, `csharp_indent_labels`, `csharp_indent_block_contents`, `csharp_indent_braces`, `csharp_indent_case_contents_when_block`
- `csharp_space_after_cast`, `csharp_space_after_keywords_in_control_flow_statements`, `csharp_space_between_parentheses`, `csharp_space_before_colon_in_inheritance_clause`, `csharp_space_after_colon_in_inheritance_clause`, `csharp_space_around_binary_operators`
- `csharp_space_between_method_declaration_parameter_list_parentheses`, `csharp_space_between_method_declaration_empty_parameter_list_parentheses`, `csharp_space_between_method_declaration_name_and_open_parenthesis`
- `csharp_space_between_method_call_parameter_list_parentheses`, `csharp_space_between_method_call_empty_parameter_list_parentheses`, `csharp_space_between_method_call_name_and_opening_parenthesis`
- `csharp_space_after_comma`, `csharp_space_before_comma`, `csharp_space_after_dot`, `csharp_space_before_dot`, `csharp_space_after_semicolon_in_for_statement`, `csharp_space_before_semicolon_in_for_statement`, `csharp_space_around_declaration_statements`, `csharp_space_before_open_square_brackets`, `csharp_space_between_empty_square_brackets`, `csharp_space_between_square_brackets`
- `csharp_preserve_single_line_blocks`, `csharp_preserve_single_line_statements`, `csharp_preferred_modifier_order`, `dotnet_sort_system_directives_first`, `dotnet_separate_import_directive_groups`

Boolean keys accept `true` or `false`. `csharp_indent_labels` accepts `flush_left`, `no_change`, or `one_less_than_current`. `csharp_space_around_binary_operators` accepts `before_and_after`, `ignore`, or `none`. List-valued keys use the values documented by the .NET EditorConfig convention. The authoritative complete Default values are emitted by `dotnet dress init`.

Layout-only rules preserve token structure and significant trivia. Syntax-transforming rules preserve the selected parse meaning by construction and skip unsafe occurrences. The full pipeline is required to be idempotent.

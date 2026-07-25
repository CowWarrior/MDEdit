# Markdown Conformance Test Document

This document exercises standard Markdown as defined by [CommonMark](https://commonmark.org/) and the
commonly-implemented extensions described in the [Markdown Guide](https://www.markdownguide.org/cheat-sheet/).

It is written to the **standard**, not to any particular editor's feature set. Constructs MDEdit does not
support are included on purpose — the point is to find the gaps. Each section notes what a conforming
renderer should do, so a mismatch is easy to spot.

---

## 1. Headings

### 1.1 ATX headings, all six levels

# Heading level 1

## Heading level 2

### Heading level 3

#### Heading level 4

##### Heading level 5

###### Heading level 6

*Expected: six distinct levels. Levels 4–6 are standard even though MDEdit's commands only cover 1–3.*

### 1.2 Closing sequences

### Heading with a closing sequence ###

*Expected: trailing `#` characters are part of the syntax and are not displayed.*

### 1.3 Setext headings

Setext heading level 1
=====================

Setext heading level 2
---------------------

*Expected: equivalent to `#` and `##`. The underline is syntax, not a horizontal rule.*

### 1.4 Not headings

\# This is an escaped hash, not a heading.

#NoSpaceAfterHash is not a heading in CommonMark.

*Expected: both render as ordinary paragraph text.*

---

## 2. Paragraphs and line breaks

This is one paragraph. It is written across
several source lines, which a conforming renderer
joins into a single flowing paragraph.

This is a second paragraph, separated by a blank line.

This line ends with two spaces  
so this line begins a new line within the same paragraph.

This line ends with a backslash\
so this line also begins a new line.

*Expected: soft wraps join; the two-space and backslash forms produce hard breaks.*

---

## 3. Emphasis

### 3.1 Basic

*Italic with asterisks* and _italic with underscores_.

**Bold with asterisks** and __bold with underscores__.

***Bold italic with asterisks*** and ___bold italic with underscores___.

### 3.2 Mixed and nested delimiters

**Bold containing _italic_ inside it.**

_Italic containing **bold** inside it._

~~Strikethrough containing **bold** inside it.~~

**Bold containing ~~strikethrough~~ inside it.**

### 3.3 Intraword and literal cases

snake_case_identifier and another_variable_name should not be italic.

A literal asterisk: \*not emphasis\*. A literal underscore: \_not emphasis\_.

2 * 3 * 4 is arithmetic, not emphasis.

An unmatched * asterisk sitting alone in a sentence.

### 3.4 Emphasis spanning source lines

This paragraph contains **emphasis that begins on one
line and closes on the next**, which CommonMark permits
because the paragraph is a single block.

*Expected: the whole span is bold. Per-line implementations typically miss this one.*

---

## 4. Blockquotes

> A simple blockquote.

> A blockquote with two paragraphs.
>
> This is the second paragraph.

> Nested blockquotes:
>
> > This is nested one level deeper.
> >
> > > And this is three levels deep.

> Blockquotes containing other blocks:
>
> ### A heading inside a quote
>
> - a list item inside a quote
> - another item
>
> ```python
> print("a code block inside a quote")
> ```
>
> And a final paragraph with **bold** and a [link](https://example.com).

> Lazy continuation: a blockquote where the following
line omits its marker but is still part of the quote.

*Expected: nesting depth is honoured, and quoted blocks render as their own construct inside the quote.*

---

## 5. Lists

### 5.1 Unordered, all three markers

- Item with a dash
- Second item
- Third item

* Item with an asterisk
* Second item

+ Item with a plus
+ Second item

*Expected: all three markers produce identical output.*

### 5.2 Ordered

1. First item
2. Second item
3. Third item

Numbering that does not start at one:

5. Item five
6. Item six
7. Item seven

All ones, which a conforming renderer numbers sequentially:

1. First item
1. Second item
1. Third item

### 5.3 Nesting

- Top level item
  - Nested one level
    - Nested two levels
      - Nested three levels
- Back to top level
  1. Ordered nested inside unordered
  2. Second nested item
     - Unordered inside ordered

### 5.4 Lists containing block content

1. An item with a following paragraph.

   This paragraph belongs to the list item above and is indented to match.

2. An item containing a code block:

   ```json
   { "indented": "inside a list item" }
   ```

3. An item containing a quote:

   > Quoted text inside a list item.

### 5.5 Loose and tight lists

- Tight list item one
- Tight list item two

- Loose list item one

- Loose list item two

*Expected: loose lists wrap items in paragraphs, adding vertical spacing.*

### 5.6 Things that look like lists but are not

The year 1986. What a great season.

2020\. Escaped, so this is a paragraph rather than a list.

A sentence with a - dash mid-line is not a list item.

---

## 6. Code

### 6.1 Inline code

Use the `printf()` function to write output.

A code span containing markdown that must stay literal: `**not bold** and _not italic_`.

Escaping backticks inside a code span: `` ` `` and ``code with a ` backtick``.

### 6.2 Indented code blocks

    This is an indented code block.
    It uses four leading spaces.
    **Markdown inside is literal.**

*Expected: rendered as a code block. This is standard syntax with no fences involved.*

### 6.3 Fenced code blocks

```
A fenced block with no language.
```

```csharp
// A fenced block with a language hint
public void Method()
{
    Console.WriteLine("Hello");
}
```

~~~javascript
// Tilde fences are equally valid
const greet = () => console.log("Hello");
~~~

A fenced block containing markdown that must stay literal:

```markdown
# Not a real heading
- not a real list
**not really bold**
```

A four-backtick fence wrapping a three-backtick fence:

````
```
nested fence, displayed literally
```
````

---

## 7. Horizontal rules

***

---

___

- - -

*Expected: four identical horizontal rules. Note the `---` form must not be read as a setext heading here,
because it is preceded by a blank line rather than a paragraph.*

---

## 8. Links

### 8.1 Inline links

An [inline link](https://www.example.com) in a sentence.

An [inline link with a title](https://www.example.com "Example Domain").

A [relative link](./another-document.md) to a sibling file.

A [link with **bold** text](https://www.example.com) inside the label.

**A [link](https://www.example.com) inside bold text.**

### 8.2 Reference links

A [reference link][ref-one] and a [second reference link][ref-two].

A [collapsed reference][] and a [shortcut reference].

[ref-one]: https://www.example.com
[ref-two]: https://www.example.com/two "With a title"
[collapsed reference]: https://www.example.com/collapsed
[shortcut reference]: https://www.example.com/shortcut

*Expected: definitions are consumed as syntax and never displayed as text.*

### 8.3 Autolinks and bare URLs

An autolink: <https://www.example.com>

An email autolink: <someone@example.com>

A bare URL that extended Markdown links automatically: https://www.example.com

A URL inside code that must not be linked: `https://www.example.com`

### 8.4 Links with awkward URLs

A [link with parentheses](https://example.com/path_(with_parens)) in the URL.

A [link with spaces](<https://example.com/path with spaces>) using angle brackets.

---

## 9. Images

An inline image: ![Alt text for the image](https://via.placeholder.com/150 "Optional title")

A reference image: ![Alt text][image-ref]

[image-ref]: https://via.placeholder.com/150

A linked image: [![Alt text](https://via.placeholder.com/60)](https://www.example.com)

*Expected: images are distinguishable from links by the leading `!`. A missing image should degrade to its
alt text rather than breaking the surrounding text.*

---

## 10. Escaping and entities

Escaped characters: \* \_ \# \[ \] \( \) \{ \} \\ \` \+ \- \. \! \| \~

Character entities: &copy; &nbsp; &amp; &lt; &gt; &hellip; &mdash;

*Expected: escapes display the literal character without its syntactic meaning.*

---

## 11. Inline HTML

This paragraph contains <em>inline HTML emphasis</em> and <strong>inline HTML strong</strong>.

<div align="center">
  A block-level HTML element.
</div>

<!-- An HTML comment, which should not be displayed as document text. -->

*Expected: HTML passes through to the renderer. An editor need not render it, but should not corrupt it.*

---

## 12. Tables *(extended syntax)*

### 12.1 Basic

| Syntax | Description |
| ----------- | ----------- |
| Header | Title |
| Paragraph | Text |

### 12.2 Column alignment

| Left aligned | Centered | Right aligned |
| :----------- | :------: | ------------: |
| Text | Text | Text |
| Longer cell content | Centered content | 1234.56 |

### 12.3 Ragged source columns

Source pipes need not line up:

| Syntax | Description | Test Text |
| :--- | :----: | ---: |
| Header | Title | Here's this |
| Paragraph | Text | And more |

### 12.4 Formatting inside cells

| Feature | Example | Notes |
| --- | --- | --- |
| Bold | **bold text** | Inline formatting is permitted in cells |
| Code | `inline_code()` | Including code spans |
| Link | [example](https://example.com) | And links |
| Escaped pipe | a \| b | A literal pipe must be escaped |

---

## 13. Task lists *(extended syntax)*

- [x] Write the press release
- [ ] Update the website
- [ ] Contact the media

Nested task lists:

- [x] Parent task complete
  - [x] Subtask complete
  - [ ] Subtask outstanding
- [ ] Parent task outstanding

A task list item with formatting:

- [ ] Review the **bold** item and the [linked](https://example.com) item

*Expected: rendered checkboxes, with `[x]` checked and `[ ]` unchecked.*

---

## 14. Highlight *(extended syntax)*

I need to highlight these ==very important words==.

Highlight combined with other emphasis: ==**bold highlighted**== and **==highlighted bold==**.

A literal double equals that is not highlight: 5 \=\= 5.

---

## 15. Superscript and subscript *(extended syntax)*

Superscript: X^2^ and E = mc^2^.

Subscript: H~2~O and CO~2~.

Combined in one line: the value of X^2^ where X~i~ is the i-th element.

*Note: `~` is also strikethrough's delimiter. `~~text~~` is strikethrough, `~text~` is subscript —
a renderer must distinguish the two by delimiter length.*

---

## 16. Emoji *(extended syntax)*

Shortcode form: That is so funny! :joy:

More shortcodes: :+1: :rocket: :warning: :book: :bug:

Literal Unicode form: That is so funny! 😂 🚀 ⚠️

A shortcode inside code that must stay literal: `:joy:`

---

## 17. Footnotes *(extended syntax)*

Here is a sentence with a footnote.[^1]

And a second one with a named footnote.[^note]

[^1]: This is the first footnote.

[^note]: This is a named footnote. It can contain **formatting**
    and span multiple lines when indented.

*Expected: markers render as superscript references, with definitions collected at the end of the document.*

---

## 18. Definition lists *(extended syntax)*

First Term
: This is the definition of the first term.

Second Term
: This is one definition of the second term.
: This is another definition of the second term.

---

## 19. Heading IDs *(extended syntax)*

### A heading with a custom ID {#custom-id}

A [link to the custom heading](#custom-id).

*Expected: the `{#custom-id}` is consumed as syntax and not displayed.*

---

## 20. Combined stress test

The following paragraph mixes many constructs at once: it has **bold**, _italic_, ***bold italic***,
~~strikethrough~~, `inline code`, a [link](https://example.com), an ![image](https://via.placeholder.com/12),
==highlight==, X^2^, H~2~O, :sparkles:, and an escaped \*literal asterisk\* — all in a single flowing
paragraph that also wraps across several source lines.

> A blockquote that contains a table:
>
> | Column A | Column B |
> | --- | --- |
> | **bold** | `code` |
>
> - [ ] and a task list
> - [x] with two items

1. An ordered item containing:
   - a nested unordered item with `code`
   - another with a [link](https://example.com)

   ```sql
   SELECT * FROM stress_test WHERE nested = 'deeply';
   ```

   > and a trailing quote inside the list item

---

## 21. Whitespace and edge cases

An empty heading:

###

A heading immediately followed by text with no blank line:
### Heading with no preceding blank line
Text immediately after the heading with no blank line.

A list immediately following a paragraph with no blank line:
- item one
- item two

Trailing whitespace on the following line is significant.   

Multiple     internal     spaces     collapse     in     rendered     output.

A line consisting only of an asterisk:

*

*Expected: these are the ambiguous cases where implementations most often diverge from the specification.*

---

*End of conformance test document.*

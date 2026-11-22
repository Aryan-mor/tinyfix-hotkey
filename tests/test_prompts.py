from prompts import (
    DEFAULT_FIX_PROMPT,
    DEFAULT_REWRITE_LANGUAGE_PROMPT,
    DEFAULT_REWRITE_PROMPT,
    DEFAULT_TRANSLATE_PROMPT,
    default_prompt_for_skill,
    render_prompt,
    strip_model_output,
)
from skills import SKILL_FIX, SKILL_REWRITE, SKILL_REWRITE_LANGUAGE, SKILL_TRANSLATE


def test_render_replaces_content_and_language():
    text = render_prompt("Fix this:\n{{content}}\nLang={{language}}", "Hi frinds", language="French")
    assert "Hi frinds" in text
    assert "{{content}}" not in text
    assert "Lang=French" in text


def test_render_appends_content_when_token_missing():
    text = render_prompt("You are a copy editor.", "Hi frinds")
    assert text.startswith("You are a copy editor.")
    assert text.endswith("Hi frinds")
    assert "Hi frinds" in text


def test_default_prompts_include_required_tokens():
    assert "{{content}}" in DEFAULT_FIX_PROMPT
    assert "{{content}}" in DEFAULT_TRANSLATE_PROMPT
    assert "{{language}}" in DEFAULT_TRANSLATE_PROMPT
    assert "{{content}}" in DEFAULT_REWRITE_PROMPT
    assert "{{content}}" in DEFAULT_REWRITE_LANGUAGE_PROMPT
    assert "{{language}}" in DEFAULT_REWRITE_LANGUAGE_PROMPT
    rendered = render_prompt(DEFAULT_FIX_PROMPT, "abc")
    assert "abc" in rendered and "{{content}}" not in rendered


def test_default_prompt_for_skill_mapping():
    assert default_prompt_for_skill(SKILL_FIX) == DEFAULT_FIX_PROMPT
    assert default_prompt_for_skill(SKILL_TRANSLATE) == DEFAULT_TRANSLATE_PROMPT
    assert default_prompt_for_skill(SKILL_REWRITE) == DEFAULT_REWRITE_PROMPT
    assert default_prompt_for_skill(SKILL_REWRITE_LANGUAGE) == DEFAULT_REWRITE_LANGUAGE_PROMPT
    from skills import DEFAULT_CUSTOM_PROMPT

    assert default_prompt_for_skill("custom-id") == DEFAULT_CUSTOM_PROMPT


def test_strip_code_fences_and_quotes():
    assert strip_model_output("```\nHello friends\n```") == "Hello friends"
    assert strip_model_output("```text\nHello friends\n```") == "Hello friends"
    assert strip_model_output('"Hello friends"') == "Hello friends"
    assert strip_model_output("'Hello friends'") == "Hello friends"
    # Do not strip quotes that belong to the text.
    assert strip_model_output('He said "hello" to friends') == 'He said "hello" to friends'
    assert strip_model_output("  already clean  ") == "already clean"
    assert strip_model_output(None) == ""
    assert strip_model_output("") == ""

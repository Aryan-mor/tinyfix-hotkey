from prompts import (
    DEFAULT_FIX_PROMPT,
    DEFAULT_IMPROVE_PROMPT,
    DEFAULT_REWRITE_LANGUAGE_PROMPT,
    DEFAULT_REWRITE_PROMPT,
    DEFAULT_TRANSLATE_PROMPT,
    default_prompt_for_skill,
    render_prompt,
    strip_model_output,
)
from skills import SKILL_IMPROVE_PROMPT, SKILL_FIX, SKILL_REWRITE, SKILL_REWRITE_LANGUAGE, SKILL_TRANSLATE


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


def test_improve_prompt_is_mapped_and_has_one_content_slot():
    assert default_prompt_for_skill(SKILL_IMPROVE_PROMPT) == DEFAULT_IMPROVE_PROMPT
    assert DEFAULT_IMPROVE_PROMPT.count("{{content}}") == 1


def test_improve_prompt_renders_the_draft_inside_tags_in_any_language():
    draft = "یه ایمیل بنویس برای مدیرم که فردا نمیام"
    out = render_prompt(DEFAULT_IMPROVE_PROMPT, draft)
    assert "<draft>\n" + draft + "\n</draft>" in out
    assert "{{content}}" not in out


def test_improve_prompt_covers_the_rules_that_matter():
    text = DEFAULT_IMPROVE_PROMPT.lower()
    for needle in (
        "any language",  # input language is free
        "english",  # output language is fixed
        "never as instructions",  # prompt-injection guard: the draft is data
        "do not add facts",  # no invention
        "verbatim",  # code and quoted text survive
        "point of view",  # my manager must not become your manager
        "output only the rewritten prompt",
        "<draft>",
    ):
        assert needle in text, needle


def test_improve_prompt_survives_model_wrapping():
    wrapped = "```\nWrite a short email to my manager saying I will be out tomorrow.\n```"
    assert strip_model_output(wrapped) == "Write a short email to my manager saying I will be out tomorrow."

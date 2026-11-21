import ctypes
import sys

import pytest

from app import INPUT, INPUT_UNION, KEYBDINPUT, MOUSEINPUT


@pytest.mark.skipif(sys.platform != "win32", reason="Windows SendInput layout only")
def test_input_struct_is_40_bytes_on_x64():
    if ctypes.sizeof(ctypes.c_void_p) != 8:
        pytest.skip("this check is for 64-bit Windows")
    # KEYBDINPUT alone is 24 bytes; without MOUSEINPUT the union is under-aligned
    # and SendInput fails with ERROR_INVALID_PARAMETER (87).
    assert ctypes.sizeof(KEYBDINPUT) == 24
    assert ctypes.sizeof(MOUSEINPUT) == 32
    assert ctypes.sizeof(INPUT_UNION) == 32
    assert ctypes.sizeof(INPUT) == 40
    assert any(name == "mi" for name, _type in INPUT_UNION._fields_)

"""Pytest configuration for the tests."""

import sys
from pathlib import Path

# Add the src directory to the path so tests can import the package
src_path = Path(__file__).parent.parent / "src"
sys.path.insert(0, str(src_path))

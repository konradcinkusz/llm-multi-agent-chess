"""Test configuration utilities."""

import os
import sys


# Ensure the src package is importable during tests
ROOT_DIR = os.path.dirname(os.path.dirname(__file__))
if ROOT_DIR not in sys.path:
    sys.path.insert(0, ROOT_DIR)


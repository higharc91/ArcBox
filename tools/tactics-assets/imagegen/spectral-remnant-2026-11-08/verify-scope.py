from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parent / 'library-refresh-272'))
from verify_scope import run

run(ROOT)

# numpy / Pillow / shapely / pyproj: from $ZGRADE_PYLIB (a pip --target folder) when set, else the system Python
import os, sys
if os.environ.get('ZGRADE_PYLIB'): sys.path.insert(0, os.environ['ZGRADE_PYLIB'])

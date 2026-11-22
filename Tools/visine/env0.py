# numpy / Pillow / shapely / pyproj from $ZGRADE_PYLIB (pip --target folder); the orthophoto / tile helpers of Tools/zgrade
import os, sys
if os.environ.get('ZGRADE_PYLIB'): sys.path.insert(0, os.environ['ZGRADE_PYLIB'])
sys.path.insert(1, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'zgrade'))

import sys
# pt.py px py [winx winy ppp] -> screen pixels
px, py = float(sys.argv[1]), float(sys.argv[2])
wx = float(sys.argv[3]) if len(sys.argv) > 3 else 120.0
wy = float(sys.argv[4]) if len(sys.argv) > 4 else 120.0
ppp = float(sys.argv[5]) if len(sys.argv) > 5 else 2.25
print(int(round((wx + px) * ppp)), int(round((wy + py) * ppp)))

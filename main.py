import os

from stl2obj import Stl2Obj

src = input("name of stl file: ")
gcode = input("name of gcode file: ")
dst = "temp.obj"

callback = lambda code: print(code)

Stl2Obj().convert(src, dst, callback)

with open(dst, "r") as obj_file:
    with open(gcode, "a") as gcode_file:
        for line in obj_file.readlines():
            gcode_file.write(";obj" + line)

print("Injected obj into gcode")

os.remove('temp.obj')


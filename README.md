# MonoInterop
Mono header file generator for c++
think of it as the exact opposite of Il2CppInterop.

# Features

MonoInterop supports Properties, fields, all of the operators, constructors
it can also Strip many of the classes by using "Root Assemblies"

# Limitations

it does not handle classes of the same Signature (same namespace, same name). rather it just removes them.

it has bugs. it may fail to call some methods or constructors. working on it.

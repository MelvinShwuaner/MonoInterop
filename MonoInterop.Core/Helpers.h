namespace System {
    class String {

        String(const MonoInterop::Char* value)
         : String((MonoObject*)mono_string_new_utf16(mono_domain_get(), (const mono_unichar2*)value, (int)MonoInterop::StrLen(value))) {}

        String(const char* utf8)
            : String((MonoObject*)mono_string_new(mono_domain_get(), utf8)) {}

        String(const MonoInterop::Char* value, int length)
            : String((MonoObject*)mono_string_new_utf16(mono_domain_get(), (const mono_unichar2*)value, length)) {}
    }
    class ValueType {
        static constexpr bool IsStruct = true;
    };
    class Object {
        #define PRIMITIVE(Name) Object(Name name) : Object(MonoInterop::BoxStruct<Name>(name)) {}
        PRIMITIVE(MonoInterop::IntPtr)
        PRIMITIVE(MonoInterop::UIntPtr)
        PRIMITIVE(MonoInterop::Int32)
        PRIMITIVE(MonoInterop::Boolean)
        PRIMITIVE(MonoInterop::UInt32)
        PRIMITIVE(MonoInterop::Int16)
        PRIMITIVE(MonoInterop::UInt16)
        PRIMITIVE(MonoInterop::Int64)
        PRIMITIVE(MonoInterop::UInt64)
        PRIMITIVE(MonoInterop::Byte)
        PRIMITIVE(MonoInterop::SByte)
        PRIMITIVE(MonoInterop::Single)
        PRIMITIVE(MonoInterop::Double)
        PRIMITIVE(MonoInterop::Char)
    };
}
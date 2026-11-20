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
}
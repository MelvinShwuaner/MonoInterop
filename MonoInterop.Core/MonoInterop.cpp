#pragma once
#include <format>
#include <mono/metadata/assembly.h>
#include <mono/metadata/appdomain.h>
#include <mono/metadata/loader.h>
#include <mono/metadata/reflection.h>
#include <initializer_list>
#include <stdexcept>
#include <string>
#include <unordered_map>
#include <mono/metadata/debug-helpers.h>
#include <android/log.h>
#define LOG(...) __android_log_print(ANDROID_LOG_DEBUG, "MonoInterop", __VA_ARGS__);
namespace MonoInterop {
    template<class X> using Generic = typename std::common_type<X>::type;
    class BaseMonoObject {
    public:
        static constexpr bool IsStruct = false;
        uint32_t handle = 0;                                   // 0 = null
        BaseMonoObject() = default;
        explicit BaseMonoObject(MonoObject* ptr) {
            if (ptr) {
                handle = mono_gchandle_new(ptr, false);
            }
        }

        // Copies get their own gchandle, otherwise two wrappers would free the same one.
        BaseMonoObject(const BaseMonoObject& o) : BaseMonoObject(o.GetObject()) {}
        BaseMonoObject(BaseMonoObject&& o) noexcept : handle(o.handle) { o.handle = 0; }
        BaseMonoObject& operator=(const BaseMonoObject& o) {
            if (this != &o) { Release(); if (auto* p = o.GetObject()) handle = mono_gchandle_new(p, false); }
            return *this;
        }
        BaseMonoObject& operator=(BaseMonoObject&& o) noexcept {
            if (this != &o) { Release(); handle = o.handle; o.handle = 0; }
            return *this;
        }
        virtual ~BaseMonoObject() { Release(); }

        MonoObject* GetObject() const { return handle ? mono_gchandle_get_target(handle) : nullptr; }
        explicit operator bool() const { return GetObject() != nullptr; }

    private:
        void Release() { if (handle) { mono_gchandle_free(handle); handle = 0;} }
    };
    struct Exception : std::runtime_error {
        BaseMonoObject object;
        explicit Exception(MonoObject* e) : std::runtime_error(Describe(e)), object(e) {}
        static std::string Describe(MonoObject* e) {
            MonoString* s = mono_object_to_string(e, nullptr);
            char* u = mono_string_to_utf8(s);
            std::string r = u; mono_free(u);
            return r;
        }
    };
    std::string AsmPath;
    inline void SetAssemblyPath(const char* path) {
        AsmPath = path;
    }
    inline void AddInternalCall(const char* sig, const void* method) {
        mono_add_internal_call(sig, method);
    }
    inline MonoImage* GetImage(const char* name) {
        static std::unordered_map<std::string, MonoImage*> cache;
        auto it = cache.find(name);
        if (it != cache.end()) return it->second;
        MonoAssembly* a = mono_domain_assembly_open(mono_domain_get(), (AsmPath + name).c_str());
        if (!a) throw std::runtime_error(std::string("assembly not found: ") + name);
        return cache[name] = mono_assembly_get_image(a);
    }

    inline MonoClass* FindClass(const char* asmName, const char* ns, const char* name) {
        MonoClass* k = mono_class_from_name(GetImage(asmName), ns, name);
        if (!k) throw std::runtime_error(std::string("class not found: ") + ns + "." + name);
        return k;
    }

    // Non-generic classes: exact lookup via metadata token (emitted by the generator from Cecil)
    inline MonoMethod* MethodByToken(MonoClass* k, uint32_t token) {
        void* iter = nullptr;
        while (MonoMethod* m = mono_class_get_methods(k, &iter))
            if (mono_method_get_token(m) == token) return m;
        throw std::runtime_error(std::format("method not found: {}::{}", mono_class_get_name(k), token));
    }
    // Closed generic classes: by name + argc (overloads need a signature match, see notes)
    inline MonoMethod* FindMethod(MonoClass* k, const char* name, int argc) {
        MonoMethod* m = mono_class_get_method_from_name(k, name, argc);
        if (!m) throw std::runtime_error(std::string("method not found: ") + name);
        return m;
    }

    inline MonoObject* Invoke(MonoMethod* m, void* self, void** args, bool virt = false) {
        if (virt && self) m = mono_object_get_virtual_method((MonoObject*)self, m);
        MonoObject* exc = nullptr;
        MonoObject* r = mono_runtime_invoke(m, self, args, &exc);
        if (exc) throw Exception(exc);
        return r;
    }

    inline MonoObject* NewObject(MonoClass* k) {
        MonoObject* o = mono_object_new(mono_domain_get(), k);
        return o;
    }
    inline MonoMethod* MakeGenericMethod(MonoMethod* open, std::initializer_list<MonoClass*> targs)
    {
        MonoDomain* d = mono_domain_get();
        MonoClass* declaring = mono_method_get_class(open);
        MonoReflectionMethod* info = mono_method_get_object(d, open, declaring);
        static MonoClass* typeCls = mono_class_from_name(mono_get_corlib(), "System", "Type");
        MonoArray* arr = mono_array_new(d, typeCls, (uintptr_t)targs.size());
        int i = 0;
        for (MonoClass* a : targs)
            mono_array_setref(arr, i++, mono_type_get_object(d, mono_class_get_type(a)));
        static MonoMethod* mgm = [] {
            static MonoClass* mi = mono_class_from_name(mono_get_corlib(), "System.Reflection", "MethodInfo");
            static MonoMethod* m = mono_class_get_method_from_name(mi, "MakeGenericMethod", 1);
            if (!m) throw std::runtime_error("MethodInfo.MakeGenericMethod not found");
            return m;
        }();

        void* args[] = { arr };
        MonoObject* result = Invoke(mgm, info, args, /*virt*/ true);
        MonoMethod* inflated = ((MonoReflectionMethod*)result)->method;
        return inflated;
    }
    // List<> + {int} -> List<int> via Type.MakeGenericType
    inline MonoClass* MakeGenericClass(MonoClass* open, std::initializer_list<MonoClass*> targs) {
        MonoDomain* d = mono_domain_get();
        static MonoClass* typeCls = FindClass("mscorlib.dll", "System", "Type");
        MonoArray* arr = mono_array_new(d, typeCls, (uintptr_t)targs.size());
        int i = 0;
        for (MonoClass* a : targs)
            mono_array_setref(arr, i++, mono_type_get_object(d, mono_class_get_type(a)));
        static MonoMethod* mgt = FindMethod(typeCls, "MakeGenericType", 1);
        void* args[] = { arr };
        MonoObject* r = Invoke(mgt, mono_type_get_object(d, mono_class_get_type(open)), args, true);
        return mono_class_from_mono_type(mono_reflection_type_get_type((MonoReflectionType*)r));
    }
    inline MonoClassField* FindField(MonoClass* k, uint32_t token) {
        MonoClassField* f = mono_class_get_field(k, token);
        if (!f) throw std::runtime_error(std::string("field not found!"));
        return f;
    }
    template<class T> T GetField(MonoObject* obj, MonoClassField* f) {
        if constexpr (std::is_base_of_v<BaseMonoObject, T>) {
            MonoObject* r = nullptr;
            mono_field_get_value(obj, f, &r);           // get: pointer to destination storage
            if constexpr (T::IsStruct)
                return T(mono_value_box(mono_domain_get(), T::ClassInit(), r));
            return T(r);
        } else {
            T v{}; mono_field_get_value(obj, f, &v); return v;
        }
    }

    template<class T> void SetField(MonoObject* obj, MonoClassField* f, const T& v) {
        if constexpr (std::is_base_of_v<BaseMonoObject, T>)
            mono_field_set_value(obj, f, v.GetObject()); // set: reference types pass the object itself
        else
            mono_field_set_value(obj, f, (void*)&v);     // value types pass a pointer to the value
    }

    template<class T> T GetStatic(MonoClass* k, MonoClassField* f) {
        MonoVTable* vt = mono_class_vtable(mono_domain_get(), k);
        if constexpr (std::is_base_of_v<BaseMonoObject, T>) {
            MonoObject* r = nullptr; mono_field_static_get_value(vt, f, &r); return T(r);
        } else { T v{}; mono_field_static_get_value(vt, f, &v); return v; }
    }

    template<class T> void SetStatic(MonoClass* k, MonoClassField* f, const T& v) {
        MonoVTable* vt = mono_class_vtable(mono_domain_get(), k);
        if constexpr (std::is_base_of_v<BaseMonoObject, T>) mono_field_static_set_value(vt, f, v.GetObject());
        else mono_field_static_set_value(vt, f, (void*)&v);
    }
    // How a C++ type maps to the Mono invoke ABI.
    // Primary template = wrapper (reference) types; value types are specialized.
    template<class T> struct MonoTraits {

        static MonoClass* GetClass() { return T::ClassInit(); }
        static void* ToArg(const T& v) {
            MonoObject* o = v.GetObject();
            if constexpr (T::IsStruct) return o ? mono_object_unbox(o) : nullptr;
            else return o;
        }
        static T FromMono(MonoObject* o) { return T(o); }   // the returned box is wrapped as-is
    };
    template<class T> struct MonoTraits<T*> {
        static MonoClass* GetClass()      { return mono_get_intptr_class(); }
        static void* ToArg(T* const& v)   { return (void*)&v; }
        static T*    FromMono(MonoObject* o) { return o ? *(T**)mono_object_unbox(o) : nullptr; }
    };
    template<class T>
    class Enumerator {
        BaseMonoObject en;                 // the IEnumerator object (own gchandle)
        T cur{};
        bool done = true;

        static MonoClass* IEnumeratorClass() {
            static MonoClass* k = FindClass("mscorlib.dll", "System.Collections", "IEnumerator");
            return k;
        }
        void Advance() {
            static MonoMethod* moveNext = FindMethod(IEnumeratorClass(), "MoveNext", 0);
            static MonoMethod* current  = FindMethod(IEnumeratorClass(), "get_Current", 0);
            MonoObject* r = Invoke(moveNext, en.GetObject(), nullptr, true);
            done = !*(bool*)mono_object_unbox(r);
            if (!done) cur = MonoTraits<T>::FromMono(Invoke(current, en.GetObject(), nullptr, true));
        }
    public:
        Enumerator() = default;                                    // end sentinel
        explicit Enumerator(MonoObject* e) : en(e), done(false) { Advance(); }

        T operator*() const { return cur; }
        Enumerator& operator++() { Advance(); return *this; }
        bool operator!=(const Enumerator& o) const { return done != o.done; }
        bool operator==(const Enumerator& o) const { return done == o.done; }
    };

    // Calls IEnumerable.GetEnumerator() on any enumerable object (virtual dispatch)
    inline MonoObject* GetEnumeratorOf(MonoObject* enumerable) {
        static MonoMethod* ge = FindMethod(
            FindClass("mscorlib.dll", "System.Collections", "IEnumerable"), "GetEnumerator", 0);
        return Invoke(ge, enumerable, nullptr, true);
    }
    template<class T, int Rank>
    class MArrayND : public BaseMonoObject {
        static_assert(Rank >= 2, "use MArray<T> for rank 1");
        static constexpr bool IsWrapper = std::is_base_of_v<BaseMonoObject, T>;

        static MonoClass *ElementClass() {
            static MonoClass *k = MonoTraits<T>::GetClass();
            return k;
        }

        static int ElemSize() {
            static int s = mono_class_array_element_size(ElementClass());
            return s;
        }

        ::MonoArray *Raw() const { return (::MonoArray *) GetObject(); }

        // Flat offset from indices, with bounds checks (arrays are zero-based when created from C++)
        template<class... Idx>
        int Offset(Idx... idx) const {
            static_assert(sizeof...(Idx) == Rank, "wrong number of indices");
            if (!Raw()) throw std::runtime_error("null array");
            const int ix[Rank] = {(int) idx...};
            int off = 0;
            for (int d = 0; d < Rank; d++) {
                int len = Length(d);
                if (ix[d] < 0 || ix[d] >= len) throw std::out_of_range("array index");
                off = off * len + ix[d];
            }
            return off;
        }

        T GetFlat(int i) const {
            void *addr = mono_array_addr_with_size(Raw(), ElemSize(), i);
            if constexpr (IsWrapper) return MonoTraits<T>::FromMono(*(MonoObject **) addr);
            else {
                T v;
                std::memcpy(&v, addr, sizeof(T));
                return v;
            }
        }

        void SetFlat(int i, const T &v) {
            if constexpr (IsWrapper)
                mono_array_setref(Raw(), i, v.GetObject());
            else std::memcpy(mono_array_addr_with_size(Raw(), ElemSize(), i), &v, sizeof(T));
        }

    public:
        static MonoClass *ClassInit() {
            static MonoClass *k = mono_array_class_get(ElementClass(), Rank);
            return k;
        }

        MArrayND() = default;

        explicit MArrayND(MonoObject *p) : BaseMonoObject(p) {
        }

        // MArrayND<int,2> a(3, 4);
        template<class... Dims, class = std::enable_if_t<sizeof...(Dims) == Rank> >
        explicit MArrayND(Dims... dims)
            : BaseMonoObject((MonoObject *) Create(std::array<uintptr_t, Rank>{(uintptr_t) dims...})) {
        }

        static ::MonoArray *Create(const std::array<uintptr_t, Rank> &lens) {
            uintptr_t l[Rank];
            for (int i = 0; i < Rank; i++) l[i] = lens[i];
            return mono_array_new_full(mono_domain_get(), ClassInit(), l, nullptr); // null lower bounds = zero-based
        }

        int Length(int dim) const {
            if (!Raw() || dim < 0 || dim >= Rank) return 0;
            return (int)Raw()->bounds[dim].length;
        }

        int TotalLength() const { return Raw() ? (int) mono_array_length(Raw()) : 0; }

        // Proxy so both `x = a(i, j)` and `a(i, j) = x` work
        class Ref {
            MArrayND &a;
            int i;

        public:
            Ref(MArrayND &arr, int idx) : a(arr), i(idx) {
            }

            operator T() const { return a.GetFlat(i); }

            Ref &operator=(const T &v) {
                a.SetFlat(i, v);
                return *this;
            }

            Ref &operator=(const Ref &o) {
                a.SetFlat(i, o.a.GetFlat(o.i));
                return *this;
            }
        };

        template<class... Idx>
        Ref operator()(Idx... idx) { return Ref(*this, Offset(idx...)); }

        template<class... Idx>
        T operator()(Idx... idx) const { return GetFlat(Offset(idx...)); }

    private:
        // Per-dimension length from the MonoArray bounds
        int DimLength(int dim) const {
            MonoArrayBounds *b = Raw()->bounds;
            return (int) b[dim].length;
        }
    };

    template<class T>
    class MArray : public BaseMonoObject {
        static constexpr bool IsWrapper = std::is_base_of_v<BaseMonoObject, T>;

        static MonoClass *ElementClass() {
            static MonoClass *k = MonoTraits<T>::GetClass();
            return k;
        }

        static int ElemSize() {
            static int s = mono_class_array_element_size(ElementClass());
            return s;
        }

        [[nodiscard]] MonoArray *Raw() const { return (::MonoArray *) GetObject(); }

        void Check(int i) const {
            if (!Raw()) throw std::runtime_error("null array");
            if (i < 0 || (uintptr_t) i >= mono_array_length(Raw())) throw std::out_of_range("array index");
        }

    public:
        static MonoClass *ClassInit() {
            static MonoClass *k = mono_array_class_get(ElementClass(), 1);
            return k;
        }

        explicit MArray(MonoObject *p) : BaseMonoObject(p) {
        } // wrap existing
        explicit MArray(int n) : BaseMonoObject((MonoObject *) mono_array_new(mono_domain_get(), ElementClass(), n)) {
        }

        MArray(std::initializer_list<T> init) : MArray((int) init.size()) {
            int i = 0;
            for (const T &v: init) Set(i++, v);
        }

        int Length() const { return Raw() ? (int) mono_array_length(Raw()) : 0; }

        T Get(int i) const {
            Check(i);
            void *addr = mono_array_addr_with_size(Raw(), ElemSize(), i);
            if constexpr (IsWrapper) {
                return MonoTraits<T>::FromMono(*(MonoObject **) addr); // null -> empty wrapper
            } else {
                T v;
                std::memcpy(&v, addr, sizeof(T));
                return v;
            }
        }

        void Set(int i, const T &v) {
            Check(i);
            if constexpr (IsWrapper) {
                mono_array_setref(Raw(), i, v.GetObject()); // setref = GC write barrier
            } else {
                std::memcpy(mono_array_addr_with_size(Raw(), ElemSize(), i), &v, sizeof(T));
            }
        }

        // Proxy so that both `x = a[i]` and `a[i] = x` work.
        class Ref {
            MArray &a;
            int i;

        public:
            Ref(MArray &arr, int idx) : a(arr), i(idx) {
            }

            operator T() const { return a.Get(i); }

            Ref &operator=(const T &v) {
                a.Set(i, v);
                return *this;
            }

            Ref &operator=(const Ref &o) {
                a.Set(i, o.a.Get(o.i));
                return *this;
            } // a[0] = a[1]
        };

        Ref profile_unused_guard() = delete; // (remove, placeholder to keep Ref declared before use)
        Ref operator[](int i) { return Ref(*this, i); }
        T operator[](int i) const { return Get(i); }

        // range-for support (iterates by value)
        struct Iter {
            const MArray *a;
            int i;
            T operator*() const { return a->Get(i); }

            Iter &operator++() {
                ++i;
                return *this;
            }

            bool operator!=(const Iter &o) const { return i != o.i; }
        };

        Iter begin() const { return {this, 0}; }
        Iter end() const { return {this, Length()}; }
    };

#define MONO_PRIMITIVE_TYPE(Name, Native, ClassGetter)                                          \
using Name = Native;                                                                \
template<> struct MonoTraits<Name> {                                         \
static MonoClass* GetClass() { return ClassGetter(); }                   \
static void* ToArg(const Name& x) { return const_cast<Name*>(&x); }      \
static Name FromMono(MonoObject* o) { return *(Name*)mono_object_unbox(o); } \
}; \

#define MONO_PRIMITIVE_WRAPPER(Name, Native, ClassGetter)                                          \
struct Name {                                                                \
Native v;                                                                \
constexpr Name() : v(0) {}                                               \
constexpr explicit Name(Native x) : v(x) {}                              \
constexpr explicit operator Native() const { return v; }                 \
constexpr bool operator==(const Name& o) const { return v == o.v; }      \
constexpr bool operator!=(const Name& o) const { return v != o.v; }      \
};                                                                           \
template<> struct MonoTraits<Name> {                                         \
static MonoClass* GetClass() { return ClassGetter(); }                   \
static void* ToArg(const Name& x) { return const_cast<Name*>(&x); }      \
static Name FromMono(MonoObject* o) { return *(Name*)mono_object_unbox(o); } \
}; \
static_assert(sizeof(Name) == sizeof(Native) && std::is_standard_layout_v<Name>, #Name " must be thin");

    MONO_PRIMITIVE_WRAPPER(IntPtr,  intptr_t, mono_get_intptr_class)
    MONO_PRIMITIVE_WRAPPER(UIntPtr, uintptr_t, mono_get_uintptr_class)
    MONO_PRIMITIVE_TYPE(Int32, int32_t, mono_get_int32_class)
    MONO_PRIMITIVE_TYPE(Boolean, bool, mono_get_boolean_class)
    MONO_PRIMITIVE_TYPE(UInt32, uint32_t, mono_get_uint32_class)
    MONO_PRIMITIVE_TYPE(Int16, int16_t, mono_get_int16_class)
    MONO_PRIMITIVE_TYPE(UInt16, uint16_t, mono_get_int16_class)
    MONO_PRIMITIVE_TYPE(Int64, int64_t, mono_get_int64_class)
    MONO_PRIMITIVE_TYPE(UInt64, uint64_t, mono_get_uint64_class)
    MONO_PRIMITIVE_TYPE(Byte, uint8_t, mono_get_byte_class)
    MONO_PRIMITIVE_TYPE(SByte, int8_t, mono_get_sbyte_class)
    MONO_PRIMITIVE_TYPE(Single, float, mono_get_single_class)
    MONO_PRIMITIVE_TYPE(Double, double, mono_get_double_class)
    MONO_PRIMITIVE_TYPE(Char, char16_t, mono_get_char_class)

    static int StrLen(const MonoInterop::Char* s) { int n = 0; while (s[n]) n++; return n; }
    template<class T> MonoObject* BoxStruct(T t) {
        return mono_value_box(mono_domain_get(), MonoTraits<T>::GetClass(), (void*)&t);
    }
}
#define MONO_ENUM_TRAITS(image, namespaze, klass, name) \
template<> struct MonoTraits<klass> { \
inline static MonoClass* GetClass() { \
static MonoClass* k = FindClass(image, namespaze, name); \
return k; \
} \
inline static void* ToArg(const klass& v) { return const_cast<klass*>(&v); } \
inline static klass FromMono(MonoObject* o) { return *(klass*)mono_object_unbox(o); } \
};
// 1. Helper macro
#define CONCAT(a, b) a##b
#define INCLUDE_IF_true(...) __VA_ARGS__
#define INCLUDE_IF_false(...)
#define OPTIONAL_SECTION(cond, ...) CONCAT(INCLUDE_IF_, cond)(__VA_ARGS__)
#define MI_UNPAREN(...) __VA_ARGS__
#define MI_VAL(...) std::remove_reference_t<__VA_ARGS__>
#define MONO_PROPERTY(Self, Name, Set, Get, ...)                                              \
struct Name##_t {                                                             \
Self* owner() const {                                                     \
return reinterpret_cast<Self*>(                                       \
const_cast<char*>(reinterpret_cast<const char*>(this)) - off());  \
}                                                                         \
static std::ptrdiff_t off() { return offsetof(Self, Name); }              \
OPTIONAL_SECTION(Get,    \
operator __VA_ARGS__() const;                          \
) \
OPTIONAL_SECTION(Set,                                            \
Name##_t& operator=(__VA_ARGS__ const& v);    \
)                                                                                \
} Name;

// Read-only: same without operator=
// Static property
#define MONO_STATIC_PROPERTY(Self, Name, Set, Get, ...)                                   \
struct Name##_t {                                                                    \
OPTIONAL_SECTION(Get,    \
operator __VA_ARGS__() const;                          \
)      \
OPTIONAL_SECTION(Set,                                                            \
Name##_t& operator=(__VA_ARGS__ const& v);                                       \
)                                                                                \
};                                                                                   \
inline static Name##_t Name{};



// call:  MONO_INDEXER(mi_Self, Item, true, (Dict_2<String, Foo>), (Int32, const String&))
#define MONO_INDEXER(Self, Name, Get, Set, T, Params, Args)                         \
struct Name##_t {                                                    \
Self* o;        \
Self* owner() const {  return o; } \
MI_UNPAREN Params; \
OPTIONAL_SECTION(Get,    \
operator MI_UNPAREN T() const;                                       \
) \
OPTIONAL_SECTION(Set, Name##_t& operator=(MI_UNPAREN T const&); )\
};                                                                       \
Name##_t operator[](MI_UNPAREN Args);
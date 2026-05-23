abstract final class Validators {
  static String? username(String? value) {
    if (value == null || value.isEmpty) return 'Kullanıcı adı gerekli.';
    if (value.length < 3) return 'En az 3 karakter olmalı.';
    if (value.length > 20) return 'En fazla 20 karakter olmalı.';
    if (!RegExp(r'^[a-zA-Z0-9_]+$').hasMatch(value)) {
      return 'Yalnızca harf, rakam ve alt çizgi kullanılabilir.';
    }
    return null;
  }

  static String? email(String? value) {
    if (value == null || value.isEmpty) return 'E-posta gerekli.';
    if (!RegExp(r'^[^@]+@[^@]+\.[^@]+$').hasMatch(value)) {
      return 'Geçerli bir e-posta adresi girin.';
    }
    return null;
  }

  static String? password(String? value) {
    if (value == null || value.isEmpty) return 'Şifre gerekli.';
    if (value.length < 8) return 'En az 8 karakter olmalı.';
    if (value.length > 72) return 'En fazla 72 karakter olmalı.';
    return null;
  }

  static String? otp(String? value) {
    if (value == null || value.isEmpty) return 'Kod gerekli.';
    if (value.length != 6) return '6 haneli kodu girin.';
    if (!RegExp(r'^\d{6}$').hasMatch(value)) return 'Yalnızca rakam girin.';
    return null;
  }

  static String? postTitle(String? value) {
    if (value == null || value.isEmpty) return 'Başlık gerekli.';
    if (value.trim().length < 10) return 'En az 10 karakter olmalı.';
    if (value.trim().length > 120) return 'En fazla 120 karakter olmalı.';
    return null;
  }

  static String? postContent(String? value) {
    if (value == null || value.isEmpty) return 'İçerik gerekli.';
    if (value.trim().length < 50) return 'En az 50 karakter olmalı.';
    if (value.trim().length > 1500) return 'En fazla 1500 karakter olmalı.';
    return null;
  }

  static String? comment(String? value) {
    if (value == null || value.isEmpty) return 'Yorum gerekli.';
    if (value.trim().length < 5) return 'En az 5 karakter olmalı.';
    if (value.trim().length > 500) return 'En fazla 500 karakter olmalı.';
    return null;
  }

  static String? passwordsMatch(String? value, String original) {
    if (value == null || value.isEmpty) return 'Şifreyi tekrar girin.';
    if (value != original) return 'Şifreler eşleşmiyor.';
    return null;
  }
}

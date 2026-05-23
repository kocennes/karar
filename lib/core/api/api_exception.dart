class ApiException implements Exception {
  const ApiException({
    required this.statusCode,
    required this.code,
    required this.message,
    this.retryAfterSeconds,
  });

  final int statusCode;
  final String code;
  final String message;
  final int? retryAfterSeconds;

  bool get isMaintenance => statusCode == 503;

  String get friendlyMessage {
    switch (statusCode) {
      case 400:
        return 'Bilgileri kontrol et.';
      case 401:
        return 'Oturum süren doldu.';
      case 403:
        return 'Bu işlem için yetkin yok.';
      case 404:
        return 'Bulunamadı.';
      case 409:
        return 'Bu işlem zaten yapılmış.';
      case 429:
        if (retryAfterSeconds != null) {
          if (retryAfterSeconds! < 60) {
            return 'Çok hızlı gidiyorsun. $retryAfterSeconds saniye sonra tekrar dene.';
          }
          final mins = (retryAfterSeconds! / 60).ceil();
          return 'Çok hızlı gidiyorsun. $mins dakika sonra tekrar dene.';
        }
        return 'Çok hızlı gidiyorsun.';
      case 500:
        return 'Bir şey ters gitti. Tekrar dene.';
      case 503:
        return 'Kısa süreli bakımdayız.';
    }
    if (code == 'NETWORK_ERROR') {
      return 'İnternet bağlantını kontrol et.';
    }
    return message.isNotEmpty ? message : 'Bir hata oluştu.';
  }

  @override
  String toString() => 'ApiException($statusCode, $code, $message)';
}

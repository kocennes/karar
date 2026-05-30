import 'dart:async';
import 'dart:convert';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/api/api_endpoints.dart';
import '../../core/providers.dart';

/// SSE üzerinden gelen notification eventleri. Sadece login kullanıcılar için bağlanır.
/// Bağlantı kopunca exponential backoff ile yeniden bağlanır (2s → 4s → … → 60s).
/// App foreground'a gelince REST fallback için [notificationsProvider].load() çağrılır.
final sseNotificationProvider =
    StreamProvider<Map<String, dynamic>>((ref) async* {
  final user = ref.watch(currentUserProvider);
  if (user == null) return;

  final apiClient = ref.read(apiClientProvider);
  var delay = const Duration(seconds: 2);

  while (true) {
    try {
      await for (final event
          in apiClient.sseStream(ApiEndpoints.notificationsEvents)) {
        try {
          final data = jsonDecode(event.data) as Map<String, dynamic>;
          final type = data['type'] as String?;
          if (type != 'ping') {
            yield data;
          }
        } catch (_) {}
        delay = const Duration(seconds: 2);
      }
    } catch (_) {
      // Bağlantı hatası — backoff sonrası tekrar dene
    }

    await Future<void>.delayed(delay);
    delay = Duration(
      seconds: (delay.inSeconds * 2).clamp(2, 60),
    );
  }
});

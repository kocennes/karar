import 'package:connectivity_plus/connectivity_plus.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

enum ConnectivityStatus { isConnected, isDisconnected, isInitial }

final connectivityProvider = StreamProvider<ConnectivityStatus>((ref) async* {
  // Web'de connectivity_plus güvenilmez sonuç dönebilir (localhost'ta none).
  // Gerçek offline tespiti için HTTP hataları (feed yüklenemedi) yeterli.
  if (kIsWeb) {
    yield ConnectivityStatus.isConnected;
    return;
  }

  final connectivity = Connectivity();

  final initial = await connectivity.checkConnectivity();
  yield initial.contains(ConnectivityResult.none)
      ? ConnectivityStatus.isDisconnected
      : ConnectivityStatus.isConnected;

  await for (final results in connectivity.onConnectivityChanged) {
    yield results.contains(ConnectivityResult.none)
        ? ConnectivityStatus.isDisconnected
        : ConnectivityStatus.isConnected;
  }
});

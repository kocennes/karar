import 'package:firebase_performance/firebase_performance.dart';

class PerformanceService {
  FirebasePerformance? _perf;

  FirebasePerformance? get _p {
    try {
      return _perf ??= FirebasePerformance.instance;
    } catch (_) {
      return null;
    }
  }

  Future<T> trace<T>(String name, Future<T> Function() fn) async {
    final t = _p?.newTrace(name);
    try {
      await t?.start();
    } catch (_) {}
    try {
      final result = await fn();
      try {
        await t?.stop();
      } catch (_) {}
      return result;
    } catch (e) {
      try {
        await t?.stop();
      } catch (_) {}
      rethrow;
    }
  }
}

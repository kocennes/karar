import 'dart:convert';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

class HistoryNotifier extends Notifier<Set<String>> {
  @override
  Set<String> build() {
    _load();
    return {};
  }

  static const _kSeenPosts = 'seen_post_ids';
  static const _kImpressionCount = 'post_impression_counts';

  Map<String, int> _impressionCounts = {};

  Future<void> _load() async {
    final prefs = await SharedPreferences.getInstance();
    final list = prefs.getStringList(_kSeenPosts) ?? [];
    state = list.toSet();

    final rawImpressions = prefs.getString(_kImpressionCount);
    if (rawImpressions != null) {
      try {
        _impressionCounts = Map<String, int>.from(jsonDecode(rawImpressions));
      } catch (_) {}
    }
  }

  Future<void> clear() async {
    state = {};
    _impressionCounts = {};
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_kSeenPosts);
    await prefs.remove(_kImpressionCount);
  }

  Future<void> markAsSeen(String postId) async {
    if (state.contains(postId)) return;
    
    final newState = {...state, postId};
    state = newState;
    
    final prefs = await SharedPreferences.getInstance();
    // Keep only last 500 to avoid huge pref file
    final list = newState.toList();
    if (list.length > 500) {
      list.removeRange(0, list.length - 500);
    }
    await prefs.setStringList(_kSeenPosts, list);
  }

  Future<void> trackImpression(String postId) async {
    _impressionCounts[postId] = (_impressionCounts[postId] ?? 0) + 1;
    
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kImpressionCount, jsonEncode(_impressionCounts));
  }

  int getImpressionCount(String postId) => _impressionCounts[postId] ?? 0;
}

final historyProvider = NotifierProvider<HistoryNotifier, Set<String>>(HistoryNotifier.new);

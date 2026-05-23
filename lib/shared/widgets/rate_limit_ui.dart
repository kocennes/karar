import 'package:flutter/material.dart';

import '../../core/api/api_exception.dart';

enum RateLimitedAction {
  vote,
  comment,
  post,
  report,
  generic,
}

abstract final class RateLimitUi {
  static bool isRateLimit(ApiException error) => error.statusCode == 429;

  static String messageFor(ApiException error, RateLimitedAction action) {
    if (!isRateLimit(error)) return error.friendlyMessage;

    return switch (action) {
      RateLimitedAction.vote =>
        'Biraz yavaşla! Kısa süre sonra tekrar oy verebilirsin.',
      RateLimitedAction.comment =>
        'Yorum limitine ulaştın. ${_retryText(error)} tekrar dene.',
      RateLimitedAction.post when error.code == 'DAILY_POST_LIMIT' =>
        'Günlük post limitine ulaştın. Yarın tekrar paylaşabilirsin.',
      RateLimitedAction.post =>
        'Paylaşım limitine ulaştın. ${_retryText(error)} tekrar dene.',
      RateLimitedAction.report =>
        'Şikayet limitine ulaştın. ${_retryText(error)} tekrar dene.',
      RateLimitedAction.generic => error.friendlyMessage,
    };
  }

  static void showSnackBar(
    BuildContext context, {
    required ApiException error,
    required RateLimitedAction action,
    VoidCallback? onRetry,
  }) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(messageFor(error, action)),
        behavior: SnackBarBehavior.floating,
        action: onRetry == null || !isRateLimit(error)
            ? null
            : SnackBarAction(
                label: 'Tekrar Dene',
                onPressed: onRetry,
              ),
      ),
    );
  }

  static String _retryText(ApiException error) {
    final seconds = error.retryAfterSeconds;
    if (seconds == null) return 'Biraz sonra';
    if (seconds < 60) return '$seconds saniye sonra';
    final minutes = (seconds / 60).ceil();
    return '$minutes dakika sonra';
  }
}

class DailyPostLimitView extends StatelessWidget {
  const DailyPostLimitView({super.key, required this.onDone});

  final VoidCallback onDone;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.hourglass_top_rounded,
              size: 64,
              color: Theme.of(context).colorScheme.primary,
            ),
            const SizedBox(height: 20),
            Text(
              'Günlük post limitine ulaştın',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.w800,
                  ),
            ),
            const SizedBox(height: 10),
            Text(
              'Yarın tekrar paylaşabilirsin.',
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                    color: Theme.of(context).colorScheme.onSurfaceVariant,
                  ),
            ),
            const SizedBox(height: 24),
            FilledButton(
              onPressed: onDone,
              child: const Text('Tamam'),
            ),
          ],
        ),
      ),
    );
  }
}

import 'package:flutter/material.dart';
import 'karar_button.dart';

class ErrorView extends StatelessWidget {
  const ErrorView({
    super.key,
    required this.message,
    this.onRetry,
    this.icon,
    this.title,
    this.isMaintenance = false,
  });

  final String message;
  final String? title;
  final IconData? icon;
  final VoidCallback? onRetry;
  final bool isMaintenance;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (isMaintenance || message.contains('bakım')) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.handyman_outlined,
                  size: 72, color: theme.colorScheme.primary),
              const SizedBox(height: 24),
              Text(
                'Kısa süreli bakımdayız',
                textAlign: TextAlign.center,
                style: theme.textTheme.headlineSmall
                    ?.copyWith(fontWeight: FontWeight.bold),
              ),
              const SizedBox(height: 12),
              Text(
                'Karar birazdan geri dönecek.',
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
              if (onRetry != null) ...[
                const SizedBox(height: 28),
                KararButton(
                  label: 'Tekrar Dene',
                  onPressed: onRetry,
                  expand: false,
                  icon: Icons.refresh,
                ),
              ],
            ],
          ),
        ),
      );
    }

    final displayTitle = title ??
        (message.contains('bulunamadı') ? 'Bulunamadı' : 'Bir Hata Oluştu');
    final displayIcon = icon ??
        (message.contains('bağlantı')
            ? Icons.wifi_off
            : message.contains('bekleyin') || message.contains('hızlı')
                ? Icons.timer_outlined
                : message.contains('bulunamadı')
                    ? Icons.search_off
                    : Icons.error_outline);

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              displayIcon,
              size: 64,
              color: theme.colorScheme.error.withValues(alpha: 0.8),
            ),
            const SizedBox(height: 16),
            Text(
              displayTitle,
              textAlign: TextAlign.center,
              style: theme.textTheme.titleLarge?.copyWith(
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 8),
            Text(
              message,
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
            if (onRetry != null) ...[
              const SizedBox(height: 24),
              KararButton(
                label: 'Tekrar Dene',
                onPressed: onRetry,
                expand: false,
                icon: Icons.refresh,
              ),
            ],
          ],
        ),
      ),
    );
  }
}

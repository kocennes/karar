import 'package:flutter/material.dart';
import '../../core/theme/karar_tokens.dart';
import 'karar_button.dart';

class OfflineView extends StatelessWidget {
  const OfflineView({super.key, this.onRetry});

  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Scaffold(
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(32),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(
                Icons.wifi_off_rounded,
                size: 80,
                color: kc.text2,
              ),
              const SizedBox(height: 24),
              Text(
                'İnternet Bağlantısı Yok',
                style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                      fontWeight: FontWeight.w900,
                    ),
              ),
              const SizedBox(height: 12),
              Text(
                'Karar vermek için internete bağlı olmalısın. Lütfen bağlantını kontrol et.',
                textAlign: TextAlign.center,
                style: TextStyle(
                  color: kc.text2,
                  height: 1.5,
                ),
              ),
              const SizedBox(height: 32),
              if (onRetry != null)
                KararButton(
                  label: 'Tekrar Dene',
                  onPressed: onRetry!,
                  icon: Icons.refresh,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

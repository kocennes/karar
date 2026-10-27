import 'package:flutter/material.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/karar_tokens.dart';

/// "Yapay Zeka Özeti" card (spec §6.1.8): brand-tinted surface, radius 24.
class AiSummaryCard extends StatelessWidget {
  const AiSummaryCard({
    super.key,
    required this.summary,
    required this.onRefresh,
    this.isRefreshing = false,
  });

  final String summary;
  final VoidCallback onRefresh;
  final bool isRefreshing;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;

    return Container(
      width: double.infinity,
      padding: const EdgeInsets.fromLTRB(18, 8, 8, 16),
      decoration: BoxDecoration(
        color: kc.primaryContainer,
        borderRadius: KararRadius.all(KararRadius.surface),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(Icons.auto_awesome_rounded, size: 20, color: kc.primary),
              const SizedBox(width: 8),
              Expanded(
                child: Semantics(
                  header: true,
                  child: Text(
                    'Yapay Zeka Özeti',
                    style: AppTypography.bodyStrong.copyWith(
                      fontWeight: FontWeight.w800,
                      color: kc.primaryText,
                    ),
                  ),
                ),
              ),
              SizedBox(
                width: 44,
                height: 44,
                child: isRefreshing
                    ? Center(
                        child: SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(
                            strokeWidth: 2,
                            color: kc.primary,
                          ),
                        ),
                      )
                    : IconButton(
                        icon: const Icon(Icons.refresh_rounded, size: 20),
                        onPressed: onRefresh,
                        tooltip: 'Özeti Yenile',
                        color: kc.primaryText,
                      ),
              ),
            ],
          ),
          Padding(
            padding: const EdgeInsets.only(right: 10),
            child: Text(
              summary,
              style: AppTypography.body.copyWith(color: kc.text1),
            ),
          ),
          const SizedBox(height: 10),
          Padding(
            padding: const EdgeInsets.only(right: 10),
            child: Text(
              'Bu özet topluluk yorumlarından otomatik üretilmiştir.',
              style: AppTypography.meta.copyWith(color: kc.text3),
            ),
          ),
        ],
      ),
    );
  }
}

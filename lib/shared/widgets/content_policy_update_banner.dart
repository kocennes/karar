import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/policy/content_policy_notice.dart';

class ContentPolicyUpdateBanner extends StatefulWidget {
  const ContentPolicyUpdateBanner({super.key});

  @override
  State<ContentPolicyUpdateBanner> createState() =>
      _ContentPolicyUpdateBannerState();
}

class _ContentPolicyUpdateBannerState extends State<ContentPolicyUpdateBanner> {
  bool _isVisible = false;

  static const _dismissPrefix = 'content_policy_notice_dismissed_';

  @override
  void initState() {
    super.initState();
    _checkStatus();
  }

  Future<void> _checkStatus() async {
    final notice = activeContentPolicyNotice;
    if (!notice.shouldShow(DateTime.now())) return;

    final prefs = await SharedPreferences.getInstance();
    final dismissed =
        prefs.getBool('$_dismissPrefix${notice.version}') ?? false;
    if (!dismissed && mounted) {
      setState(() => _isVisible = true);
    }
  }

  Future<void> _dismiss() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setBool(
      '$_dismissPrefix${activeContentPolicyNotice.version}',
      true,
    );
    if (mounted) {
      setState(() => _isVisible = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (!_isVisible) return const SizedBox.shrink();

    final scheme = Theme.of(context).colorScheme;

    return Container(
      width: double.infinity,
      color: scheme.secondaryContainer,
      padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
      child: SafeArea(
        bottom: false,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(
              Icons.policy_outlined,
              size: 22,
              color: scheme.onSecondaryContainer,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    activeContentPolicyNotice.title,
                    style: TextStyle(
                      color: scheme.onSecondaryContainer,
                      fontWeight: FontWeight.w700,
                      fontSize: 13,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    activeContentPolicyNotice.summary,
                    style: TextStyle(
                      color: scheme.onSecondaryContainer,
                      fontSize: 12,
                      height: 1.35,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 8,
                    runSpacing: 4,
                    children: [
                      OutlinedButton(
                        onPressed: () => context.push('/legal/content-policy'),
                        style: OutlinedButton.styleFrom(
                          visualDensity: VisualDensity.compact,
                          side: BorderSide(color: scheme.onSecondaryContainer),
                          foregroundColor: scheme.onSecondaryContainer,
                        ),
                        child: const Text('Değişiklikleri Gör'),
                      ),
                      TextButton(
                        onPressed: _dismiss,
                        style: TextButton.styleFrom(
                          visualDensity: VisualDensity.compact,
                          foregroundColor: scheme.onSecondaryContainer,
                        ),
                        child: const Text('Kapat'),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            IconButton(
              icon: const Icon(Icons.close, size: 18),
              color: scheme.onSecondaryContainer,
              tooltip: 'Kapat',
              visualDensity: VisualDensity.compact,
              onPressed: _dismiss,
            ),
          ],
        ),
      ),
    );
  }
}

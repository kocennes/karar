import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../config/remote_config_service.dart';
import '../providers.dart';

class SessionVoteTracker extends AutoDisposeNotifier<void> {
  @override
  void build() {}

  int get _breakThreshold {
    final value = ref.read(remoteConfigProvider).getInt(RemoteConfigKeys.wellbeingSessionLimit);
    return value > 0 ? value : 20;
  }

  void onVoteCast(BuildContext context) {
    final tracker = ref.read(sessionTrackerProvider);
    final threshold = _breakThreshold;
    if (tracker.votesCastInSession > 0 && tracker.votesCastInSession % threshold == 0) {
      _showBreakInterstitial(context, tracker.votesCastInSession);
    }
  }

  void _showBreakInterstitial(BuildContext context, int voteCount) {
    final theme = Theme.of(context);

    showModalBottomSheet<void>(
      context: context,
      isDismissible: true,
      enableDrag: true,
      builder: (ctx) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const Text('⌛', style: TextStyle(fontSize: 48)),
              const SizedBox(height: 16),
              Text(
                'Bir Mola Vermek İster Misin?',
                style: theme.textTheme.headlineSmall?.copyWith(fontWeight: FontWeight.bold),
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 12),
              Text(
                'Bu oturumda $voteCount karar verdin 🧑‍⚖️\nZihinsel bir mola vermek iyi gelebilir.',
                textAlign: TextAlign.center,
                style: const TextStyle(fontSize: 16, height: 1.5),
              ),
              const SizedBox(height: 24),
              Row(
                children: [
                  Expanded(
                    child: OutlinedButton(
                      onPressed: () => Navigator.pop(ctx),
                      child: const Text('Devam Et'),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: FilledButton(
                      onPressed: () {
                        Navigator.pop(ctx);
                        // In a real app, we might minimize or show a different screen.
                        // Here we just close the modal.
                      },
                      child: const Text('Mola Ver'),
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}

final sessionVoteTrackerProvider =
    AutoDisposeNotifierProvider<SessionVoteTracker, void>(SessionVoteTracker.new);

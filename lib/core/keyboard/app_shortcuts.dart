import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:go_router/go_router.dart';

class _SearchIntent extends Intent {
  const _SearchIntent();
}

class _GoBackIntent extends Intent {
  const _GoBackIntent();
}

class AppShortcuts extends StatelessWidget {
  const AppShortcuts({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    // Klavye kısayolları yalnızca web ve masaüstünde anlamlı
    if (!kIsWeb && defaultTargetPlatform == TargetPlatform.android ||
        !kIsWeb && defaultTargetPlatform == TargetPlatform.iOS) {
      return child;
    }

    return Shortcuts(
      shortcuts: const {
        SingleActivator(LogicalKeyboardKey.slash): _SearchIntent(),
        SingleActivator(LogicalKeyboardKey.escape): _GoBackIntent(),
      },
      child: Actions(
        actions: {
          _SearchIntent: CallbackAction<_SearchIntent>(
            onInvoke: (_) {
              final router = GoRouter.of(context);
              if (router.routeInformationProvider.value.uri.path != '/search') {
                context.push('/search');
              }
              return null;
            },
          ),
          _GoBackIntent: CallbackAction<_GoBackIntent>(
            onInvoke: (_) {
              // Esc: Overlay/modal varsa kapat, yoksa route'ta geri git
              final navigator = Navigator.maybeOf(context);
              if (navigator != null && navigator.canPop()) {
                navigator.pop();
              }
              return null;
            },
          ),
        },
        child: child,
      ),
    );
  }
}

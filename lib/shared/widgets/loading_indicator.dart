import 'package:flutter/material.dart';

class LoadingIndicator extends StatelessWidget {
  const LoadingIndicator({super.key, this.size = 24});

  final double size;

  @override
  Widget build(BuildContext context) => Center(
        child: SizedBox(
          width: size,
          height: size,
          child: CircularProgressIndicator(
            strokeWidth: size < 20 ? 2 : 3,
          ),
        ),
      );
}

class FullScreenLoading extends StatelessWidget {
  const FullScreenLoading({super.key});

  @override
  Widget build(BuildContext context) => const Scaffold(
        body: LoadingIndicator(size: 36),
      );
}

import 'package:flutter/material.dart';
import 'package:telehealth_app/app/router/app_router.dart';

class TelehealthApp extends StatelessWidget {
  const TelehealthApp({super.key});

  // This widget is the root of your application.
  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      debugShowCheckedModeBanner: false,
       title: 'Telehealth',
      routerConfig: appRouter,
    );
  }
}